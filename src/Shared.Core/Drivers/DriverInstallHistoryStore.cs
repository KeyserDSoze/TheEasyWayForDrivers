using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>Local audit of user-requested installation batches, not a per-driver success report.</summary>
public sealed record DriverInstallHistoryEntry(
    DateTimeOffset TimestampUtc,
    string DriverTitles,
    string Outcome,
    bool RebootRequired,
    string Message);

public sealed class DriverInstallHistoryStore(string filePath)
{
    public const int MaxEntries = 200;

    public IReadOnlyList<DriverInstallHistoryEntry> Read()
    {
        if (!File.Exists(filePath))
            return [];

        var entries = JsonSerializer.Deserialize<List<DriverInstallHistoryEntry>>(
            File.ReadAllText(filePath)) ?? [];

        return entries
            .OrderByDescending(e => e.TimestampUtc)
            .Take(MaxEntries)
            .ToArray();
    }

    public void Add(DriverInstallHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        IReadOnlyList<DriverInstallHistoryEntry> previous;
        try
        {
            previous = Read();
        }
        catch (JsonException)
        {
            // Keep the invalid data available for manual diagnostics instead of
            // overwriting it. A unique backup avoids clobbering older recoveries.
            var damagedPath = filePath + ".corrupt-" + Guid.NewGuid().ToString("N");
            File.Move(filePath, damagedPath);
            previous = [];
        }

        var entries = previous.Prepend(entry)
            .OrderByDescending(e => e.TimestampUtc)
            .Take(MaxEntries)
            .ToArray();

        var folder = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        Directory.CreateDirectory(folder);
        var temporary = filePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
            File.Move(temporary, filePath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
