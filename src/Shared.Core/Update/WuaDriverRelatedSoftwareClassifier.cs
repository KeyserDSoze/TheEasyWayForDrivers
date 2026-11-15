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

    // #466: every pass status, not just Completed, must carry possible
    // counts. A failed/partial snapshot claiming -1 results or more newly
    // accepted than returned is structurally corrupt, not a verified query.
    private static bool HasImpossibleCounters(DriverSearchPassStatus pass) =>
        pass.Candidates < 0 || pass.NewUniqueCandidates < 0 ||
        pass.NewUniqueCandidates > pass.Candidates;

    // The software plan never attempts a NotRegistered query. Preserve
    // the explicit older Microsoft Update sentinel only for legacy IPC
    // compatibility. A stranger's NotRegistered label (even with zero
    // counts) is not evidence that our software search plan was complete.
    private static bool IsNeutralNotRegistered(DriverSearchPassStatus pass) =>
        pass.Outcome == "NotRegistered" &&
        pass.Candidates == 0 && pass.NewUniqueCandidates == 0 &&
        (pass.Source is "Microsoft Update online · componenti software" or
            "Microsoft Update software");

    public static bool IsVerifiableSoftwarePassStatus(
        DriverSearchPassStatus pass,
        IReadOnlyList<DriverSearchPassStatus> sources)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(sources);
        return IsKnownSoftwareSource(pass.Source) &&
            pass.Outcome != "NotRegistered" &&
            WindowsDriverCandidateMerge.IsRecognizedPassOutcome(pass.Outcome) &&
            !HasImpossibleCounters(pass) &&
            sources.Count(other => string.Equals(other.Source, pass.Source,
                StringComparison.Ordinal)) == 1;
    }

    public static bool HasUnverifiableSoftwarePassStatuses(
        IReadOnlyList<DriverSearchPassStatus> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return sources.Any(pass =>
        {
            // NotRegistered is neutral ONLY for canonical/legacy
            // Microsoft Update markers with zero counters. Unknown source
            // labels or invalid outcomes must make the optional software
            // search visibly incomplete, without poisoning Type=Driver.
            if (!seen.Add(pass.Source))
                return true;
            if (pass.Outcome == "NotRegistered")
                return !IsNeutralNotRegistered(pass);
            return !IsKnownSoftwareSource(pass.Source) ||
                !WindowsDriverCandidateMerge.IsRecognizedPassOutcome(pass.Outcome) ||
                HasImpossibleCounters(pass);
        });
    }

    public static bool HasCompletedKnownSoftwarePass(DriverSearchReport? report) =>
        report?.RelatedSoftwareSources is { } sources &&
        sources.Any(pass => pass.Succeeded &&
            IsVerifiableSoftwarePassStatus(pass, sources));

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
            .Where(pass => pass.Succeeded &&
                IsVerifiableSoftwarePassStatus(pass,
                    report.RelatedSoftwareSources))
            .Select(pass => pass.Source)
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
        "AMD", "Realtek", "Qualcomm", "Broadcom", "MediaTek"
    ];

    private static readonly string[] DriverContexts =
    [
        "Driver", "Bluetooth", "Wireless", "Wi-Fi", "WiFi",
        "Network", "Networking", "Graphics", "Display", "Audio", "Chipset",
        "Extension", "SoftwareComponent", "Software Component",
        "Platform Controller", "Platform Controllers", "PROSet",
        "Control Panel"
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
            HasUnsafeTitleMetadata(title))
            return null;

        // #482: a vendor or component substring is NOT a vendor identity:
        // "NotIntel Driver", "NVIDIAware Network" and "Networkish" are
        // unrelated labels. Keep legitimate punctuation (Intel(R),
        // PROSet/Wireless, SoftwareComponent, Networking) supported.
        if (!Vendors.Any(v => HasWordBoundary(title, v)) ||
            !DriverContexts.Any(c => HasWordBoundary(title, c)) ||
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

    // A WUA Type=Software title is not a trusted hardware identity.
    // It must not reflect local identifiers or contain invisible text
    // that changes how a user reads the advisory source. Validate before
    // trimming and before preserving any portion in a public report.
    private static bool HasUnsafeTitleMetadata(string title)
    {
        if (title.Contains(":/", StringComparison.Ordinal) ||
            title.Contains("//", StringComparison.Ordinal) ||
            title.Contains('\\') || title.Contains('@') ||
            title.Contains("PRIVATE_", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var marker in new[]
        {
            "VEN_", "VID_", "DEV_", "PID_", "SUBSYS_", "REV_",
            "INSTANCEID", "SERIALNUMBER"
        })
            if (title.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;

        return title.Any(ch =>
            char.IsControl(ch) || char.IsSurrogate(ch) ||
            ch == '\uFFFD' || // replacement char: malformed UTF-16 input
            (char.IsWhiteSpace(ch) && ch != ' ') ||
            char.GetUnicodeCategory(ch) is
                System.Globalization.UnicodeCategory.Format or
                System.Globalization.UnicodeCategory.LineSeparator or
                System.Globalization.UnicodeCategory.ParagraphSeparator);
    }

    // Unicode letters/digits must be bounded on both sides. Punctuation
    // remains a valid separator in real WUA driver-related software names.
    private static bool HasWordBoundary(string title, string word)
    {
        var startAt = 0;
        while (startAt < title.Length)
        {
            var index = title.IndexOf(word, startAt,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;
            var after = index + word.Length;
            if ((index == 0 || !char.IsLetterOrDigit(title[index - 1])) &&
                (after == title.Length || !char.IsLetterOrDigit(title[after])))
                return true;
            startAt = index + 1;
        }
        return false;
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
