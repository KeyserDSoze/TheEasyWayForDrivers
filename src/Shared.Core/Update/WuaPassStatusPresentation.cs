using System.Globalization;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Safe, user-facing per-source WUA Type=Driver statuses for guided Home.
/// Preserves the already audited COM outcome without exposing exception
/// text, private WSUS/proxy URLs, PnP identities or update GUIDs.
/// </summary>
public static class WuaPassStatusPresentation
{
    public const int MaximumVisibleSources = 12;

    private static readonly HashSet<string> KnownNames =
        WindowsDriverSearchPlan.Create(DriverSearchMode.Comprehensive, true)
            .Select(pass => pass.Name)
            .Append(WindowsDriverSearchPlan.NotRegisteredStatus().Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Create(DriverSearchReport? report)
    {
        if (report is null)
            return ["Windows Update: non interrogato oppure richiesta non riuscita."];
        if (report.Sources.Count == 0)
            return ["Windows Update: nessuna fonte interrogata e verificata; " +
                    "disponibilità driver sconosciuta."];

        var lines = new List<string>();
        foreach (var source in report.Sources.Take(MaximumVisibleSources))
        {
            // Only our immutable query-plan names are ever rendered. A
            // malformed/legacy IPC payload must never leak a local path,
            // server hostname or hardware ID via the Source/Detail fields.
            var name = KnownNames.Contains(source.Source)
                ? source.Source
                : "Fonte Windows non riconosciuta";
            var state = source.Outcome switch
            {
                "Completed" =>
                    $"ricerca completata, {Math.Max(0, source.Candidates)} " +
                    $"risultati restituiti, {Math.Max(0, source.NewUniqueCandidates)} " +
                    "ID nuovi rispetto alle fonti precedenti.",
                "Partial" =>
                    "ricerca PARZIALE: dati non convalidati dalla fonte; " +
                    "eventuali titoli sono solo indizi, NON installabili. " +
                    "Disponibilità di ulteriori driver sconosciuta.",
                "PolicyRestricted" =>
                    "ricerca BLOCCATA da criteri Windows; fonte non verificata, " +
                    "non significa zero driver.",
                "NetworkError" =>
                    "ricerca non riuscita (rete/proxy/server); fonte non verificata, " +
                    "non prova che il PC sia offline o privo di driver.",
                "Failed" =>
                    "ricerca NON RIUSCITA; disponibilità dei driver " +
                    "di questa fonte sconosciuta.",
                "NotRegistered" =>
                    "servizio Microsoft Update non registrato, NON interrogato; " +
                    "nessuna configurazione Windows modificata.",
                _ =>
                    "esito non riconosciuto: fonte NON verificata, " +
                    "non dedurre assenza di driver."
            };

            // Keep the wording aligned with actual GUID provenance and
            // the copyable report: foreign/duplicated sources or impossible
            // counters never become evidence of a completed query.
            var verifiedStatus = WindowsDriverCandidateMerge
                .IsVerifiablePassStatus(source, report.Sources);
            if (!verifiedStatus &&
                WindowsDriverCandidateMerge.IsRecognizedPassOutcome(
                    source.Outcome))
                state = report.Sources.Count(item => string.Equals(
                    item.Source, source.Source,
                    StringComparison.OrdinalIgnoreCase)) > 1
                    ? "stati della stessa fonte duplicati/contraddittori: " +
                      "fonte NON verificata; disponibilità driver sconosciuta."
                    : "fonte NON verificata (origine sconosciuta o " +
                      "dati incoerenti); disponibilità driver sconosciuta.";
            // Unknown outcome already has an explicit non-verified message
            // from the switch above. Do not erase that distinction, and
            // never display its untrusted AttemptedAtUtc timestamp.

            var time = verifiedStatus && source.Outcome != "NotRegistered" &&
                source.AttemptedAtUtc is { } attemptedAt
                    ? " · tentata " + attemptedAt.ToUniversalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture) + " UTC"
                    : string.Empty;
            lines.Add($"{name}: {state}{time}");
        }

        if (report.Sources.Count > MaximumVisibleSources)
            lines.Add($"{report.Sources.Count - MaximumVisibleSources} altre " +
                "fonti non mostrate: il riepilogo è parziale.");

        return lines;
    }
}
