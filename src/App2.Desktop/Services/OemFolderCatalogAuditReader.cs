using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Runs a deliberately bounded, explicit Windows read-only CAT signature and
/// INF membership review for already-extracted OEM files. Windows SDK
/// SignTool is optional; its absence is "not verified", never success.
/// No file paths, device IDs, signer strings or order numbers are returned.
/// </summary>
public static class OemFolderCatalogAuditReader
{
    public static async Task<OemFolderCatalogAuditSummary.Report> InspectAsync(
        string directory, IReadOnlyList<DriverInfo> devices,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Verifica OEM disponibile su Windows.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        var plan = await Task.Run(() => OemFolderCatalogAuditPlan.Create(
            directory, devices, RuntimeInformation.OSArchitecture.ToString(),
            timeout.Token), timeout.Token);

        var signatures = new Dictionary<string, CatalogTrustReport.Status>(
            StringComparer.OrdinalIgnoreCase);
        var checkedRows = new List<OemFolderCatalogAuditSummary.Row>();
        // First validate every selected CAT/INF and preview its dependency
        // names (bounded, read-only). Only after we know which INF pairs are
        // trustworthy can the eight global SignTool payload checks be fairly
        // spread across NVIDIA/Killer/Realtek instead of all being consumed
        // by the first large INF in traversal order.
        var prepared = new List<(
            string CatalogPath, OemReferencedPayloadAudit.Plan Payload,
            bool TrustedInf)>(OemFolderCatalogAuditPlan.MaxChecks);
        foreach (var check in plan.Checks)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (!signatures.TryGetValue(check.CatalogPath, out var catalogStatus))
            {
                try
                {
                    var result = await WindowsCatalogSignatureReader.InspectAsync(
                        check.CatalogPath, timeout.Token);
                    catalogStatus = result.SignatureStatus;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or InvalidOperationException or
                    System.ComponentModel.Win32Exception or JsonException)
                {
                    catalogStatus = CatalogTrustReport.Status.NotVerified;
                }
                signatures.Add(check.CatalogPath, catalogStatus);
            }

            var membership = CatalogMemberVerification.NotChecked();
            if (catalogStatus == CatalogTrustReport.Status.ValidCatalogSignature)
            {
                try
                {
                    membership = await WindowsInfCatalogMembershipReader.VerifyAsync(
                        check.InfPath, check.CatalogPath, timeout.Token);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or InvalidOperationException or
                    System.ComponentModel.Win32Exception)
                {
                    membership = CatalogMemberVerification.NotChecked();
                }
            }

            checkedRows.Add(new OemFolderCatalogAuditSummary.Row(
                catalogStatus, membership.Result));

            // Preview potential CopyFiles dependencies even when SignTool is
            // missing, so "not verified" is distinguishable from a missing
            // or unresolved file. Never turn an INF/CAT success into proof
            // that the entire package has been audited.
            OemReferencedPayloadAudit.Plan payloadPlan;
            try
            {
                payloadPlan = await Task.Run(() =>
                    OemReferencedPayloadAudit.Prepare(
                        check.InfPath, check.CatalogPath,
                        RuntimeInformation.OSArchitecture.ToString(),
                        timeout.Token), timeout.Token);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                InvalidDataException or System.Security.SecurityException)
            {
                payloadPlan = OemReferencedPayloadAudit.Plan.NotAnalyzed();
            }

            var trustedInf = catalogStatus ==
                CatalogTrustReport.Status.ValidCatalogSignature &&
                membership.Result ==
                CatalogMemberVerification.Verdict.VerifiedInfMember;
            prepared.Add((check.CatalogPath, payloadPlan, trustedInf));
        }

        var allocations = OemReferencedPayloadAudit.AllocateChecks(
            prepared.Select(entry => entry.TrustedInf
                ? entry.Payload.CandidatePaths.Count : 0).ToArray());
        var results = prepared.Select(entry =>
            new List<DriverPayloadAuditPolicy.Outcome>(
                entry.Payload.CandidatePaths.Count)).ToArray();
        var deferred = new int[prepared.Count];
        for (var index = 0; index < prepared.Count; index++)
        {
            var entry = prepared[index];
            if (!entry.TrustedInf)
            {
                // A CAT with no validated INF membership authorizes ZERO
                // payload checks: these files remain explicitly unverified.
                results[index].AddRange(Enumerable.Repeat(
                    DriverPayloadAuditPolicy.Outcome.NotVerified,
                    entry.Payload.CandidatePaths.Count));
            }
            else
            {
                deferred[index] =
                    entry.Payload.CandidatePaths.Count - allocations[index];
            }
        }

        // Execute in actual rounds as well as allocating by rounds. If
        // Windows verification takes too long, a single INF cannot use
        // several checks before another eligible INF gets its first.
        // All results are still discarded on cancellation/timeout.
        for (var round = 0;
             round < OemReferencedPayloadAudit.MaxFilesPerInf; round++)
        {
            for (var index = 0; index < prepared.Count; index++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (round >= allocations[index])
                    continue;

                var entry = prepared[index];
                DriverPayloadAuditPolicy.Outcome status;
                try
                {
                    var outcome = await WindowsInfCatalogMembershipReader.VerifyPayloadAsync(
                        entry.Payload.CandidatePaths[round],
                        entry.CatalogPath,
                        Path.GetDirectoryName(entry.CatalogPath)!,
                        timeout.Token);
                    status = outcome.Result switch
                    {
                        CatalogMemberVerification.Verdict.VerifiedInfMember =>
                            DriverPayloadAuditPolicy.Outcome.VerifiedMember,
                        CatalogMemberVerification.Verdict.Rejected =>
                            DriverPayloadAuditPolicy.Outcome.Rejected,
                        _ => DriverPayloadAuditPolicy.Outcome.NotVerified
                    };
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    System.Security.SecurityException)
                {
                    status = DriverPayloadAuditPolicy.Outcome.NotVerified;
                }
                results[index].Add(status);
            }
        }

        var payloadReviews = prepared.Select((entry, index) =>
            new OemReferencedPayloadAudit.Reviewed(
                entry.Payload, results[index], deferred[index])).ToArray();

        var summary = OemFolderCatalogAuditSummary.Create(plan, checkedRows);
        return summary with
        {
            PayloadAudit = OemReferencedPayloadAudit.Summarize(payloadReviews)
        };
    }
}
