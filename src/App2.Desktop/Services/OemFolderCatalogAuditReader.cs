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
        }
        return OemFolderCatalogAuditSummary.Create(plan, checkedRows);
    }
}
