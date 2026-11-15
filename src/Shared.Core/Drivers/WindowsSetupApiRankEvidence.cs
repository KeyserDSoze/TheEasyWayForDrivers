using System.Globalization;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only, language-independent rank evidence obtained from Windows
/// SetupDiGetDriverInstallParams for drivers ALREADY in the local Windows
/// driver list for one selected PnP device. This is not an OEM ZIP rank,
/// a catalog query or authority to stage/install anything.
/// </summary>
public static class WindowsSetupApiRankEvidence
{
    public const int MaxEnumeratedDrivers = 256;
    public const int MaxDisplayedDrivers = 32;
    public const int MaxLabelCharacters = 72;

    public enum State { Available, NotVerified }

    public sealed record Driver(
        uint Rank,
        string Description,
        string Provider,
        ulong DriverVersion,
        // Native SP_DRVINFO_DATA.DriverDate (INF DriverVer date),
        // NOT a download date or evidence of a newer online update.
        // Optional for older callers and unreadable native timestamps.
        DateTimeOffset? DriverDateUtc = null);

    /// <summary>
    /// Convert the native FILETIME read by SetupAPI without fabricating
    /// dates for zero, invalid or out-of-range values. UTC dates are
    /// formatted without time-of-day in the diagnostic display.
    /// </summary>
    public static DateTimeOffset? FromNativeFileTime(long fileTime)
    {
        if (fileTime <= 0)
            return null;
        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public sealed record Report(
        State Status,
        string DisplayText,
        int DriversEnumerated = 0,
        int DriversShown = 0,
        bool Incomplete = true)
    {
        public bool IsNewDriverUpdate => false;
        public bool IsInstallable => false;
        public bool HasVerifiedExternalOemRanking => false;
    }

    public static Report NotVerified() => new(
        State.NotVerified,
        "Ranking SetupAPI locale non verificabile. " +
        "Nessuna conclusione sull'assenza di driver o aggiornamenti. " +
        "I pacchetti ZIP/CAB OEM esterni non sono classificati da questa verifica.");

    /// <summary>
    /// Never expose PnP Instance IDs, Hardware IDs, paths, quoted escape
    /// sequences or other arbitrary native driver-provided text. Official
    /// provider/description strings are labels, not trusted executable input.
    /// </summary>
    public static string SafeLabel(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "N/D";

        // Keep friendly, international native labels within the UI cap.
        // Validation MUST inspect the COMPLETE original field first. A
        // private suffix after char 72, or an invisible Unicode separator
        // in the middle of PRIVATE_, must not escape by truncation/scrub.
        var cleaned = new string(source.Trim().Take(MaxLabelCharacters + 1)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '.' or ',' or
                '_' or '-' or '(' or ')' or '+' or '&' ? ch : ' ')
            .ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ',
            StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0)
            return "N/D";

        if (HasPrivateNativeText(source))
            return "[etichetta oscurata]";
        if (cleaned.Length > MaxLabelCharacters)
            cleaned = cleaned[..MaxLabelCharacters].TrimEnd();
        return cleaned;
    }

    // Description/ProviderName come from native driver metadata, NOT a
    // privacy-reviewed manufacturer allowlist. A partial PnP fragment or
    // arbitrary private/order marker can still identify a machine or user.
    // The raw full string is examined; invisible characters cause the
    // entire nonempty label to be hidden, never repaired to look public.
    private static readonly string[] PrivateNativeMarkers =
    [
        "VEN_", "VID_", "SUBSYS_", "DEV_", "PID_", "REV_", "FUNC_",
        "MI_", "INSTANCEID", "SERIALNUMBER", "PRIVATE_", "SECRET_",
        "ORDER_"
    ];

    private static bool HasPrivateNativeText(string source) =>
        source.IndexOfAny(['\\', '/', ':', '@']) >= 0 ||
        PrivateNativeMarkers.Any(marker =>
            source.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
        source.Any(ch => char.IsControl(ch) || char.IsSurrogate(ch) ||
            ch == '\uFFFD' || (char.IsWhiteSpace(ch) && ch != ' ') ||
            char.GetUnicodeCategory(ch) is
                UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator);


    public static Report Summarize(
        IReadOnlyList<Driver> drivers, bool complete)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        if (drivers.Count > MaxEnumeratedDrivers)
            throw new InvalidDataException("Troppi driver SetupAPI nel riepilogo.");

        if (drivers.Count == 0)
            return NotVerified();

        // Windows ranks matching local packages by numeric rank first,
        // then the INF DriverVer DATE, and finally the driver version.
        // Alphabetical provider/description are display-only tie-breakers
        // after all Windows' known comparison fields. With an unknown date
        // the ordering is merely illustrative, not Windows' confirmed pick.
        var ordered = drivers
            .OrderBy(entry => entry.Rank)
            .ThenByDescending(entry =>
                entry.DriverDateUtc?.UtcDateTime.Ticks ?? long.MinValue)
            .ThenByDescending(entry => entry.DriverVersion)
            .ThenBy(entry => SafeLabel(entry.Provider),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => SafeLabel(entry.Description),
                StringComparer.OrdinalIgnoreCase)
            .Take(MaxDisplayedDrivers)
            .ToArray();

        var extra = drivers.Count - ordered.Length;
        var lines = new List<string>
        {
            "Ranking Windows SetupAPI (driver COMPATIBILI già presenti nel sistema; sola lettura).",
            "Rank numerico minore = precedenza migliore NELL'ELENCO LOCALE di Windows.",
            "A parità di rank: data INF DriverVer più recente, poi versione maggiore.",
            "La sequenza visualizzata è diagnostica, NON conferma quale driver Windows abbia installato.",
            "Non classifica gli ZIP/CAB OEM esterni e non prova che esistano nuovi aggiornamenti.",
            $"Driver locali enumerati: {drivers.Count}; righe mostrate: {ordered.Length}."
        };
        if (!complete)
            lines.Add("Verifica PARZIALE: alcuni nodi/rank non sono stati letti; nessuna conclusione negativa.");
        if (extra > 0)
            lines.Add($"{extra} altre righe non mostrate per il limite diagnostico.");

        if (drivers.GroupBy(entry => entry.Rank).Any(group =>
            group.Count() > 1 && group.Any(entry => entry.DriverDateUtc is null)))
            lines.Add("AVVISO: rank uguali con data INF non verificata; " +
                "l'ordine diagnostico NON prova la preferenza di Windows.");

        foreach (var item in ordered)
        {
            var version = FormatVersion(item.DriverVersion);
            var date = item.DriverDateUtc is { } driverDate
                ? driverDate.ToUniversalTime().ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
                : "N/D";
            lines.Add($"Rank 0x{item.Rank:X8} · {SafeLabel(item.Provider)} · " +
                $"{SafeLabel(item.Description)} · data INF {date} · " +
                $"versione {version}");
        }

        return new Report(State.Available,
            string.Join(Environment.NewLine, lines),
            drivers.Count, ordered.Length, !complete);
    }

    public static string FormatVersion(ulong value) =>
        $"{(ushort)(value >> 48)}.{(ushort)(value >> 32)}." +
        $"{(ushort)(value >> 16)}.{(ushort)value}";
}
