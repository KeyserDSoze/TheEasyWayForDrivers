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

        // A single source is queried once by the search plan. When an
        // IPC snapshot reports the same source twice (even with case
        // differences), neither entry is a verified result. Display
        // this explicitly rather than printing a misleading Completed.
        var duplicateSources = report.Sources
            .Where(source => KnownNames.Contains(source.Source))
            .GroupBy(source => source.Source, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            if (duplicateSources.Contains(source.Source))
                state = "stati della stessa fonte duplicati/contraddittori: " +
                    "fonte NON verificata; disponibilità driver sconosciuta.";

            var time = source.Outcome != "NotRegistered" &&
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
