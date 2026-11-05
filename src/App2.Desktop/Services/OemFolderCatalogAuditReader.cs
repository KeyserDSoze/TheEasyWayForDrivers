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
        var payloadReviews = new List<OemReferencedPayloadAudit.Reviewed>();
        var remainingPayloadChecks = OemReferencedPayloadAudit.MaxFilesPerReview;
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

            var statuses = new List<DriverPayloadAuditPolicy.Outcome>();
            var deferred = 0;
            var trustedInf = catalogStatus ==
                CatalogTrustReport.Status.ValidCatalogSignature &&
                membership.Result ==
                CatalogMemberVerification.Verdict.VerifiedInfMember;
            foreach (var payloadPath in payloadPlan.CandidatePaths)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!trustedInf)
                {
                    statuses.Add(DriverPayloadAuditPolicy.Outcome.NotVerified);
                    continue;
                }
                if (remainingPayloadChecks <= 0)
                {
                    deferred++;
                    continue;
                }
                remainingPayloadChecks--;

                try
                {
                    var outcome = await WindowsInfCatalogMembershipReader.VerifyPayloadAsync(
                        payloadPath, check.CatalogPath,
                        Path.GetDirectoryName(check.CatalogPath)!,
                        timeout.Token);
                    statuses.Add(outcome.Result switch
                    {
                        CatalogMemberVerification.Verdict.VerifiedInfMember =>
                            DriverPayloadAuditPolicy.Outcome.VerifiedMember,
                        CatalogMemberVerification.Verdict.Rejected =>
                            DriverPayloadAuditPolicy.Outcome.Rejected,
                        _ => DriverPayloadAuditPolicy.Outcome.NotVerified
                    });
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or
                    InvalidDataException or InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    System.Security.SecurityException)
                {
                    statuses.Add(DriverPayloadAuditPolicy.Outcome.NotVerified);
                }
            }
            payloadReviews.Add(new OemReferencedPayloadAudit.Reviewed(
                payloadPlan, statuses, deferred));
        }

        var summary = OemFolderCatalogAuditSummary.Create(plan, checkedRows);
        return summary with
        {
            PayloadAudit = OemReferencedPayloadAudit.Summarize(payloadReviews)
        };
    }
}
