namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// An update offered by WUA with Type='Software', not a Windows driver update.
/// Title-based classification is merely a conservative discovery hint; no
/// hardware, OEM applicability, binary signature or installation is inferred.
/// </summary>
public sealed record WuaDriverRelatedSoftware(string UpdateId, string Title, string SearchSource)
{
    public bool IsDriverUpdate => false;
    public bool IsInstallable => false;
    public string Detail => SearchSource +
        " · software del produttore da verificare; NON driver confermato o installabile.";
}

public static class WuaDriverRelatedSoftwareClassifier
{
    public const int MaxItems = 32;
    public const int MaxRawSearchResults = 4000;

    private static readonly string[] Vendors =
    [
        "Intel", "Killer", "NVIDIA", "Advanced Micro Devices",
        "AMD ", "Realtek", "Qualcomm", "Broadcom", "MediaTek"
    ];

    private static readonly string[] DriverContexts =
    [
        "Driver", "Bluetooth", "Wireless", "Wi-Fi", "WiFi",
        "Network", "Graphics", "Display", "Audio", "Chipset",
        "Extension", "SoftwareComponent", "Software Component",
        "Platform Controller", "PROSet", "Control Panel"
    ];

    private static readonly string[] ExcludedContexts =
    [
        "Firmware", "BIOS", "UEFI", "Security Intelligence",
        "Defender Antivirus", "Windows Cumulative", "Office",
        ".NET Framework"
    ];

    public static WuaDriverRelatedSoftware? TryCreate(
        string? updateId, string? title, string searchSource)
    {
        if (!Guid.TryParse(updateId, out _) ||
            string.IsNullOrWhiteSpace(title) || title.Length > 200 ||
            (title.Contains(":/", StringComparison.Ordinal) ||
             title.Contains("//", StringComparison.Ordinal)) ||
            title.Any(c => char.IsControl(c) || c is '\\' or
                '\u202E' or '\u202D' or '\u2066' or '\u2067' or
                '\u2068' or '\u2069'))
            return null;

        if (!Vendors.Any(v => title.Contains(v, StringComparison.OrdinalIgnoreCase)) ||
            !DriverContexts.Any(c => title.Contains(c, StringComparison.OrdinalIgnoreCase)) ||
            ExcludedContexts.Any(c => title.Contains(c, StringComparison.OrdinalIgnoreCase)))
            return null;

        // Only trusted source labels emitted by our own WUA search plan.
        if (searchSource is not ("Windows configurato · componenti software" or
            "Windows Update online · componenti software" or
            "Microsoft Update online · componenti software"))
            return null;

        return new WuaDriverRelatedSoftware(updateId!, title.Trim(), searchSource);
    }

    /// <summary>Preserve one WUA identity across queried services, without
    /// mixing it with Type='Driver' candidates or installing it.</summary>
    public static int Merge(
        IDictionary<string, WuaDriverRelatedSoftware> destination,
        IEnumerable<WuaDriverRelatedSoftware> results,
        out int omitted,
        ISet<string>? previouslyOmittedIds = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(results);
        omitted = 0;
        var added = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in results)
        {
            if (!seen.Add(item.UpdateId) ||
                destination.ContainsKey(item.UpdateId) ||
                previouslyOmittedIds?.Contains(item.UpdateId) == true)
                continue;
            if (destination.Count >= MaxItems)
            {
                if (previouslyOmittedIds is null ||
                    previouslyOmittedIds.Add(item.UpdateId))
                    omitted++;
                continue;
            }

            destination.Add(item.UpdateId, item);
            added++;
        }
        return added;
    }
}
