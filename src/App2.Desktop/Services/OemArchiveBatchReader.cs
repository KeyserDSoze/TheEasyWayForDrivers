using System.IO;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Explicitly selected, bounded OEM ZIP/CAB batch. Every source uses the
/// existing read-only, fail-closed archive parser. No order number, remote
/// request, EXE/MSI, driver staging or package installation.
/// A single failed package rejects the entire batch (no partial success).
/// </summary>
public static class OemArchiveBatchReader
{
    public static async Task<LocalOemArchiveBatchSummary.Report> CompareAsync(
        IReadOnlyList<string> paths,
        IReadOnlyList<DriverInfo> devices,
        string architecture, int osMajor, int osMinor, int osBuild,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(devices);
        if (paths.Count is < 2 or > LocalOemArchiveBatchSummary.MaximumArchives)
            throw new InvalidDataException("Seleziona da 2 a 12 archivi ZIP/CAB.");

        cancellationToken.ThrowIfCancellationRequested();
        if (devices.Count > 2048)
            throw new InvalidDataException("Inventario PnP oltre il limite.");

        // Validate EVERY file BEFORE opening any archive. The source list
        // can contain a mixed ZIP/CAB set, but never EXE/MSI or reparse links.
        var accepted = new List<(string FullPath, string Extension)>(paths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long cumulative = 0;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Percorso archivio mancante.");

            var fullPath = Path.GetFullPath(path);
            if (!seen.Add(fullPath))
                throw new InvalidDataException("Archivio OEM selezionato più volte.");

            var file = new FileInfo(fullPath);
            var extension = file.Extension.ToLowerInvariant();
            if (extension is not (".zip" or ".cab"))
                throw new NotSupportedException(
                    "Solo ZIP e CAB. EXE/MSI non vengono mai eseguiti.");
            if (!file.Exists || file.Length is < 4 or > OemArchiveInspector.MaxArchiveBytes ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "Archivio OEM mancante, collegato o oltre il limite.");

            cumulative = checked(cumulative + file.Length);
            if (cumulative > LocalOemArchiveBatchSummary.MaximumTotalArchiveBytes)
                throw new InvalidDataException(
                    "Archivi OEM oltre il limite complessivo di 4 GiB.");
            accepted.Add((fullPath, extension));
        }

        // The per-CAB extractor has a 120s budget; the entire opt-in batch
        // also has a global budget to prevent a hung or unreasonable scan.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var reports = new List<LocalOemDriverComparison.Report>(accepted.Count);

        foreach (var (path, extension) in accepted)
        {
            timeout.Token.ThrowIfCancellationRequested();
            LocalOemDriverComparison.Report report;
            if (extension == ".cab")
            {
                report = await LocalOemCabComparison.CompareAsync(
                    path, devices, architecture, osMajor, osMinor, osBuild,
                    timeout.Token);
            }
            else
            {
                report = await Task.Run(() =>
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    using var input = new FileStream(path, FileMode.Open,
                        FileAccess.Read, FileShare.Read, 65536,
                        FileOptions.SequentialScan);
                    return LocalOemDriverComparison.Compare(
                        input, devices, architecture, osMajor, osMinor, osBuild);
                }, timeout.Token);
            }
            reports.Add(report);
        }

        timeout.Token.ThrowIfCancellationRequested();
        return LocalOemArchiveBatchSummary.Combine(reports);
    }
}
