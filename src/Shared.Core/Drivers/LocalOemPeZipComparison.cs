using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only OEM PE self-extracting ZIP subset -> existing INF/PNP evidence.
/// Does NOT execute any byte of the EXE or promote drivers to WUA/Update All.
/// Rejects unsupported 7z/NSIS/Inno/other executable package formats.
/// </summary>
public static class LocalOemPeZipComparison
{
    public static LocalOemDriverComparison.Report Compare(
        Stream executable, IReadOnlyList<DriverInfo> devices,
        string architecture, int osMajor, int osMinor, int osBuild)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(devices);
        using var zip = OemPeZipOverlay.Open(executable);
        var report = LocalOemDriverComparison.Compare(
            zip, devices, architecture, osMajor, osMinor, osBuild);

        // Original INF basenames may carry account/order/user identifiers.
        // Label source positions only. No original EXE name/path is emitted.
        var displayed = report.Findings.Select((finding, index) =>
            finding with { InfName = $"EXE ZIP · INF {index + 1:D3}" })
            .ToArray();

        return report with
        {
            Findings = displayed,
            Notes = report.Notes.Concat(
            [
                "EXE letto SENZA esecuzione: solo overlay ZIP standard " +
                "dopo le sezioni PE. 7z/NSIS/Inno non supportati.",
                "File INF confrontati in memoria, nessun SYS/DLL/installer avviato. " +
                "Nessuna prova di firma CAT, ranking SetupAPI o compatibilità OEM."
            ]).ToArray()
        };
    }
}
