using TheEasyWayForDrivers.Core.Models;

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

    // Only actual Type=Software WUA query names qualify; driver-pass
    // success or a spoofed source can never validate software title hints.
    private static readonly HashSet<string> KnownSoftwareSources =
        WindowsDriverSearchPlan.CreateRelatedSoftware(
                DriverSearchMode.Comprehensive, microsoftUpdateRegistered: true)
            .Select(pass => pass.Name)
            .ToHashSet(StringComparer.Ordinal);

    public static bool IsKnownSoftwareSource(string? source) =>
        source is not null && KnownSoftwareSources.Contains(source);

    public static bool HasCompletedKnownSoftwarePass(DriverSearchReport? report) =>
        report?.RelatedSoftwareSources?
            .Where(pass => IsKnownSoftwareSource(pass.Source))
            .GroupBy(pass => pass.Source, StringComparer.Ordinal)
            .Any(group => group.Count() == 1 && group.All(pass => pass.Succeeded))
        == true;

    /// <summary>
    /// Only advisory, non-installable Type=Software evidence from a
    /// successfully COMPLETED matching software pass is displayed.
    /// Validate raw/legacy IPC data again at this boundary. Duplicate
    /// or contradictory pass statuses for the same source fail closed.
    /// </summary>
    public static IReadOnlyList<WuaDriverRelatedSoftware> ConfirmedForReport(
        DriverSearchReport? report)
    {
        if (report is null || report.SoftwareReviewItems.Count == 0 ||
            report.SoftwareReviewItems.Count > MaximumStagedItems ||
            report.RelatedSoftwareSources is null)
            return [];

        var completed = report.RelatedSoftwareSources
            .Where(pass => IsKnownSoftwareSource(pass.Source))
            .GroupBy(pass => pass.Source, StringComparer.Ordinal)
            .Where(group => group.Count() == 1 && group.All(pass => pass.Succeeded))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (completed.Count == 0)
            return [];

        var verified = report.SoftwareReviewItems
            .Where(hint => completed.Contains(hint.SearchSource))
            .Select(hint => TryCreate(hint.UpdateId, hint.Title,
                hint.SearchSource))
            .Where(hint => hint is not null)
            .Select(hint => hint!)
            .ToArray();
        // Retain 32-slot cross-vendor fairness for legacy over-budget
        // snapshots, but only AFTER completed-source and title checks.
        return SelectFair(verified).Hints;
    }

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
        if (!Guid.TryParse(updateId, out var parsedId) ||
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

        // UpdateIdentity is a GUID, not a source-specific string. D/B/N/P
        // forms must take ONE Type=Software review slot across WU/MU.
        return new WuaDriverRelatedSoftware(
            parsedId.ToString("D"), title.Trim(), searchSource);
    }


    // Only the three WUA Type=Software passes in Comprehensive mode can
    // contribute, each with at most 4000 structurally verified COM rows.
    public const int MaximumStagedItems = 3 * MaxRawSearchResults;

    public sealed record FairSelection(
        IReadOnlyList<WuaDriverRelatedSoftware> Hints, int Omitted);

    /// <summary>
    /// After EVERY completed source has been enumerated, reserve a fair
    /// share of the 32 review-only slots for the major vendors. Previously
    /// the first WUA source could fill all 32 with Intel, silently hiding
    /// genuine Killer/NVIDIA/Realtek Type=Software IDs in later sources.
    /// All entries here are already validated by TryCreate. This method
    /// does not produce DriverUpdateInfo or approve installation.
    /// </summary>
    public static FairSelection SelectFair(
        IReadOnlyList<WuaDriverRelatedSoftware> verifiedHints)
    {
        ArgumentNullException.ThrowIfNull(verifiedHints);
        if (verifiedHints.Count > MaximumStagedItems)
            throw new InvalidDataException(
                "Troppi suggerimenti software WUA per una selezione verificabile.");

        // Raw/legacy IPC hints can bypass TryCreate. Normalize *before*
        // the fairness calculation, otherwise equivalent D/B/N/P GUIDs
        // steal distinct slots from later NVIDIA/Killer/Realtek rows.
        // Never treat a non-GUID as WUA software evidence.
        var unique = verifiedHints
            .Where(hint => Guid.TryParse(hint.UpdateId, out _))
            .Select(hint => hint with
            {
                UpdateId = Guid.Parse(hint.UpdateId).ToString("D")
            })
            .DistinctBy(hint => hint.UpdateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // No reordering or wasted slots on small collections.
        if (unique.Length <= MaxItems)
            return new(unique, 0);

        // Group order follows the FIRST observed vendor, so configured
        // Windows sources keep priority within their group. Round-robin
        // prevents one vendor/source from monopolizing all review rows.
        var groups = unique
            .GroupBy(hint => VendorGroup(hint.Title))
            .Select(group => group.ToArray())
            .ToArray();
        var chosen = new List<WuaDriverRelatedSoftware>(MaxItems);
        for (var depth = 0; chosen.Count < MaxItems; depth++)
        {
            var found = false;
            foreach (var group in groups)
            {
                if (depth >= group.Length)
                    continue;
                found = true;
                chosen.Add(group[depth]);
                if (chosen.Count == MaxItems)
                    break;
            }
            if (!found)
                break;
        }
        return new(chosen, unique.Length - chosen.Count);
    }

    private static string VendorGroup(string title)
    {
        // Killer branding often appears alongside Intel: give it a
        // distinct bucket before evaluating the shared Intel vendor name.
        if (title.Contains("Killer", StringComparison.OrdinalIgnoreCase))
            return "Killer";
        if (title.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
            return "NVIDIA";
        if (title.Contains("Realtek", StringComparison.OrdinalIgnoreCase))
            return "Realtek";
        if (title.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase))
            return "Qualcomm";
        if (title.Contains("Broadcom", StringComparison.OrdinalIgnoreCase))
            return "Broadcom";
        if (title.Contains("MediaTek", StringComparison.OrdinalIgnoreCase))
            return "MediaTek";
        if (title.Contains("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("AMD ", StringComparison.OrdinalIgnoreCase))
            return "AMD";
        if (title.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return "Intel";
        return "Other";
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

        // The destination or omitted list may come from a legacy IPC
        // snapshot where the same GUID was stored in B, N or P syntax.
        // Compare the parsed GUID VALUE across all three collections.
        // The destination is bounded by MaxItems in the normal flow.
        var known = destination.Keys
            .Where(key => Guid.TryParse(key, out _))
            .Select(Guid.Parse)
            .ToHashSet();
        var alreadyOmitted = previouslyOmittedIds is null
            ? null
            : previouslyOmittedIds
                .Where(key => Guid.TryParse(key, out _))
                .Select(Guid.Parse)
                .ToHashSet();
        var seen = new HashSet<Guid>();

        foreach (var item in results)
        {
            if (!Guid.TryParse(item.UpdateId, out var parsed) ||
                !seen.Add(parsed) || known.Contains(parsed) ||
                alreadyOmitted?.Contains(parsed) == true)
                continue;

            var canonical = parsed.ToString("D");
            if (destination.Count >= MaxItems)
            {
                if (previouslyOmittedIds is null || alreadyOmitted!.Add(parsed))
                {
                    previouslyOmittedIds?.Add(canonical);
                    omitted++;
                }
                continue;
            }

            destination.Add(canonical, item with { UpdateId = canonical });
            known.Add(parsed);
            added++;
        }
        return added;
    }
}
