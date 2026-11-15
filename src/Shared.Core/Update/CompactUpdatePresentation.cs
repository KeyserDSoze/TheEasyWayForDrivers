using System.Globalization;
using System.Text.RegularExpressions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// User-facing text for a single WUA result row. This deliberately shares
/// the exact ordinary/quick-install predicate with the installer UI; an
/// optional, firmware or untrusted-source row is never called "recommended".
/// UI automation labels include only a bounded, checked WUA title, never
/// HardwareId, DeviceId, UpdateId, serial or the IPC source string.
/// </summary>
public static class CompactUpdatePresentation
{
    // A WUA display title is not a trusted device-identity channel.
    // Avoid narrating embedded GUIDs from legacy snapshots in an extra
    // UIA field. The short, bounded pattern has a finite timeout.
    private static readonly Regex GuidInTitle = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        @"[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static string KindText(DriverUpdateInfo update) =>
        KindText(update, QuickUpdatePolicy.IsEligible(update));

    public static string KindText(DriverUpdateInfo update, bool canQuickInstall)
    {
        ArgumentNullException.ThrowIfNull(update);

        // A genuine firmware/BIOS/UEFI row cannot be installed by the
        // normal WUA driver action even when WUA called it "ordinary".
        if (QuickUpdatePolicy.IsFirmwareOrBios(
            update.Title, update.DriverClass))
            return "Firmware / BIOS";
        if (update.IsHidden)
            return "Nascosto";
        if (update.IsAdvancedCandidate)
            return "Avanzato";
        if (CompactResultFilter.IsOptionalReview(update))
            return "Facoltativo";

        // "Consigliato" must mean the visible row actually passes our
        // quick-install policy; raw flags and unverified origins do not.
        return canQuickInstall && QuickUpdatePolicy.IsEligible(update)
            ? "Consigliato"
            : "Da verificare";
    }

    public static string SelectionAccessibleName(DriverUpdateInfo update) =>
        SelectionAccessibleName(update, QuickUpdatePolicy.IsEligible(update));

    public static string SelectionAccessibleName(
        DriverUpdateInfo update, bool canQuickInstall)
    {
        ArgumentNullException.ThrowIfNull(update);
        var title = SafeWuaTitle(update.Title);
        return canQuickInstall && QuickUpdatePolicy.IsEligible(update)
            ? $"Seleziona {title} per aggiornare tramite Windows Update"
            : $"Selezione non disponibile per {title}: " +
              $"{KindText(update, false)}, solo dettagli";
    }

    public static string ActionAccessibleName(DriverUpdateInfo update) =>
        ActionAccessibleName(update, QuickUpdatePolicy.IsEligible(update));

    public static string ActionAccessibleName(
        DriverUpdateInfo update, bool canQuickInstall)
    {
        ArgumentNullException.ThrowIfNull(update);
        var title = SafeWuaTitle(update.Title);
        return canQuickInstall && QuickUpdatePolicy.IsEligible(update)
            ? $"Aggiorna {title} tramite Windows Update"
            : $"Apri dettagli di {title}: {KindText(update, false)}, " +
              "nessuna installazione rapida";
    }

    private static string SafeWuaTitle(string? title)
    {
        // COM safety already bounds titles, but legacy IPC callers can
        // pass arbitrary strings. Do not leak control/format characters,
        // paths, hardware IDs, GUIDs or raw private metadata through an
        // extra accessibility field. The visual row remains unchanged.
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512 ||
            title.Contains('\\') || title.Contains("://", StringComparison.Ordinal) ||
            GuidInTitle.IsMatch(title) ||
            title.Any(c => char.IsControl(c) ||
                char.IsSurrogate(c) || char.GetUnicodeCategory(c) is
                    UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator))
            return "driver Windows senza nome verificato";

        // Keep the audible label useful in large search results without
        // reading a whole 512-character release-description aloud.
        var clean = title.Trim();
        return clean.Length <= 120 ? clean : clean[..120] + "…";
    }
}
