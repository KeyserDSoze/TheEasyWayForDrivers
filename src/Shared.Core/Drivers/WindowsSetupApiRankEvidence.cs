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
        ulong DriverVersion);

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

        // A path, device ID, URL or driver instance text should never be
        // printed. We only display short text labels using a strict subset.
        var cleaned = new string(source.Trim().Take(MaxLabelCharacters + 1)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '.' or ',' or
                '_' or '-' or '(' or ')' or '+' or '&' ? ch : ' ')
            .ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ',
            StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0)
            return "N/D";
        if (cleaned.Length > MaxLabelCharacters)
            cleaned = cleaned[..MaxLabelCharacters].TrimEnd();

        // Components of a hardware instance ID are sensitive even without
        // the leading bus prefix; never render probable instance strings.
        if (source.Contains('\\') || source.Contains('/') ||
            source.Contains(':') || source.Contains('@') ||
            source.Contains("VEN_", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("VID_", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("SUBSYS_", StringComparison.OrdinalIgnoreCase))
            return "[etichetta oscurata]";
        return cleaned;
    }

    public static Report Summarize(
        IReadOnlyList<Driver> drivers, bool complete)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        if (drivers.Count > MaxEnumeratedDrivers)
            throw new InvalidDataException("Troppi driver SetupAPI nel riepilogo.");

        if (drivers.Count == 0)
            return NotVerified();

        var ordered = drivers
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => SafeLabel(entry.Provider),
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => SafeLabel(entry.Description),
                StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(entry => entry.DriverVersion)
            .Take(MaxDisplayedDrivers)
            .ToArray();

        var extra = drivers.Count - ordered.Length;
        var lines = new List<string>
        {
            "Ranking Windows SetupAPI (driver COMPATIBILI già presenti nel sistema; sola lettura).",
            "Rank numerico minore = precedenza migliore NELL'ELENCO LOCALE di Windows.",
            "Non classifica gli ZIP/CAB OEM esterni e non prova che esistano nuovi aggiornamenti.",
            $"Driver locali enumerati: {drivers.Count}; righe mostrate: {ordered.Length}."
        };
        if (!complete)
            lines.Add("Verifica PARZIALE: alcuni nodi/rank non sono stati letti; nessuna conclusione negativa.");
        if (extra > 0)
            lines.Add($"{extra} altre righe non mostrate per il limite diagnostico.");

        foreach (var item in ordered)
        {
            var version = FormatVersion(item.DriverVersion);
            lines.Add($"Rank 0x{item.Rank:X8} · {SafeLabel(item.Provider)} · " +
                $"{SafeLabel(item.Description)} · versione {version}");
        }

        return new Report(State.Available,
            string.Join(Environment.NewLine, lines),
            drivers.Count, ordered.Length, !complete);
    }

    public static string FormatVersion(ulong value) =>
        $"{(ushort)(value >> 48)}.{(ushort)(value >> 32)}." +
        $"{(ushort)(value >> 16)}.{(ushort)value}";
}
