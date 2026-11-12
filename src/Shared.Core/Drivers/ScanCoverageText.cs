using System.Globalization;
using System.Text;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Human-readable, deliberately non-identifying evidence for support/QA.
/// Do not include raw device instance IDs, Hardware IDs, serial numbers,
/// computer name, installed package paths or model identifiers.
/// </summary>
public static class ScanCoverageText
{
    // The copyable QA report is an anonymous diagnostic artifact. Source
    // names and vendor labels are untrusted IPC fields: short strings can
    // still be a computer name, serial, WSUS hostname or order number.
    // Render only canonical source labels produced by our own query plans.
    private static readonly HashSet<string> KnownDriverSources =
        WindowsDriverSearchPlan.Create(DriverSearchMode.Comprehensive, true)
            .Select(pass => pass.Name)
            .Append(WindowsDriverSearchPlan.NotRegisteredStatus().Source)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KnownSoftwareSources =
        WindowsDriverSearchPlan.CreateRelatedSoftware(
                DriverSearchMode.Comprehensive, true)
            .Select(pass => pass.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Canonical labels are exactly those returned by ChooseSource in
    // VendorSourceDiscovery. An unknown vendor is never echoed to the copy.
    private static readonly HashSet<string> KnownVendorLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Killer / Intel", "NVIDIA", "Intel", "AMD", "Realtek",
            "Broadcom", "Qualcomm / Atheros", "MediaTek",
            "Logitech", "Razer"
        };

    private static string SafeLabel(
        string? source, HashSet<string> known, string fallback) =>
        source is not null && known.TryGetValue(source, out var canonical)
            ? canonical : fallback;

    public static string Create(
        DriverSearchReport? report,
        IEnumerable<VendorCoverageRow> vendorRows,
        int deviceCount,
        int problemCount,
        int manufacturerRecommended,
        IReadOnlyList<DriverInfo>? inventoriedDrivers = null,
        bool inventoryComplete = false,
        VendorInventoryCoverage.Report? vendorCoverage = null)
    {
        ArgumentNullException.ThrowIfNull(vendorRows);

        var output = new StringBuilder();
        output.AppendLine("OmegaDrive — copertura della ricerca driver");
        output.AppendLine("Report aggregato; nessun seriale, hardware ID o nome del PC.");
        output.AppendLine($"Dispositivi Windows: {Math.Max(0, deviceCount)}");
        output.AppendLine($"Problemi Windows: {Math.Max(0, problemCount)}");
        output.AppendLine($"Pacchetti consigliati dal produttore: {Math.Max(0, manufacturerRecommended)}");
        // Win32_PnPSignedDriver is optional enrichment. A failed lookup does
        // not mean Windows reports a missing driver or no available update.
        if (inventoryComplete && inventoriedDrivers is not null)
        {
            var metadataUnverified = inventoriedDrivers.Count(
                device => !device.DriverMetadataQuerySucceeded);
            if (metadataUnverified > 0)
                output.AppendLine(
                    $"Metadati driver installati non verificati: {metadataUnverified} " +
                    "dispositivi; non significa driver mancanti.");
            var problemCodeUnverified = inventoriedDrivers.Count(
                device => !device.PnpProblemCodeVerified);
            if (problemCodeUnverified > 0)
                output.AppendLine(
                    $"Codici problema Windows PnP non verificati: " +
                    $"{problemCodeUnverified} dispositivi; non significa " +
                    "assenza di errori né driver mancanti.");
            // Aggregate only: never export the recovered PnP identity itself.
            var identityRecovered = inventoriedDrivers.Count(
                device => device.PnpIdsRecoveredFromSignedDriver);
            if (identityRecovered > 0)
                output.AppendLine(
                    $"Identificativi PnP integrati da metadati driver locali: " +
                    $"{identityRecovered} dispositivi. " +
                    "Migliora la correlazione WUA per ID, NON conferma nuovi aggiornamenti.");

            var nativeRecovered = inventoriedDrivers.Count(
                device => device.PnpIdsRecoveredFromSetupApi);
            if (nativeRecovered > 0)
                output.AppendLine(
                    $"Identificativi PnP recuperati con SetupAPI Windows locale: " +
                    $"{nativeRecovered} dispositivi. " +
                    "Hardware ID e Compatible ID letti in sola lettura; " +
                    "NON significa driver nuovi o installabili.");
        }
        // The audit already applies the strict WUA GUID/source predicate,
        // merges two completed WUA passes by GUID and enforces bounded IPC
        // budgets. Reuse THAT result for the copyable header and the
        // incomplete-ID caveat. Counting report.Updates raw rows here
        // contradicted the audit below (and Home) for duplicate GUIDs,
        // untrusted OEM records and malformed legacy IPC payloads.
        // This does not validate any OEM package or installation.
        var associationAudit = WuaPnpAssociationAudit.Create(
            inventoriedDrivers ?? [], report, inventoryComplete);
        output.AppendLine(!associationAudit.SearchVerified
            ? "Windows Update: ricerca non verificata"
            : associationAudit.OverBudget
                ? "Windows Update: conteggio candidati non valutabile (limite diagnostico)"
                : $"Windows Update: {associationAudit.ConfirmedGuids} candidati distinti");

        // These are per-GUID PnP associations that WUA could not fully
        // enumerate, not missing driver downloads. Duplicated same-GUID
        // entries count once and external/unknown sources count zero.
        if (associationAudit.SearchVerified &&
            !associationAudit.OverBudget &&
            associationAudit.IncompleteWuaIds > 0)
            output.AppendLine(
                $"Candidati WUA con associazioni dispositivo incomplete: " +
                $"{associationAudit.IncompleteWuaIds}. " +
                "Non significa zero driver per le periferiche non correlate.");
        // Why did WUA return only two offers while PnP contains other
        // vendors? Distinguish supply from PnP correlation without raw
        // device IDs, GUIDs, installed versions or private titles.
        output.AppendLine(associationAudit.Text);
        // Offline WUA only reads historical metadata from the local
        // Windows datastore. Its titles are never fresh online offers;
        // do not copy cache GUIDs, device IDs, titles or paths to support.
        if (report?.CachedDriverSources is { Count: > 0 })
        {
            var completed = report.CachedDriverSources?
                .Count(source => source.Succeeded) ?? 0;
            if (completed == 0)
                output.AppendLine(
                    "Cache Windows locale (storico): ricerche offline non verificate. " +
                    "Questo non significa che manchino driver.");
            else
                output.AppendLine(
                    "Cache Windows locale (storico, SOLO informativa): " +
                    $"{report.CachedDriverReviewItems.Count} titoli distinti, " +
                    $"{report.CachedDriverReviewItems.Count(h => h.WasInstalled == true)} " +
                    "contrassegnati come installati nella cache, " +
                    $"{report.CachedDriverReviewItems.Count(h => h.WasInstalled == false)} " +
                    "non installati; " +
                    $"{report.CachedDriverHintsOmitted} oltre il limite. " +
                    (report.CachedDriverSearchFailed
                        ? "Fonti offline parziali/non verificate. "
                        : "") +
                    "NON sono nuove offerte online né driver installabili.");
        }
        output.AppendLine("Fonti WUA:");
        if (report is null || report.Sources.Count == 0)
        {
            output.AppendLine("- nessuna fonte verificata");
        }
        else
        {
            foreach (var source in report.Sources.Take(12))
            {
                // The copyable QA report must not call a duplicated or
                // foreign Completed IPC row "interrogata": those GUIDs
                // are already excluded from the verified WUA update list.
                var verifiedStatus = WindowsDriverCandidateMerge
                    .IsVerifiablePassStatus(source, report.Sources);
                var label = !verifiedStatus
                    ? "fonte NON verificata: dati duplicati, sconosciuti o incoerenti"
                    : source.Outcome switch
                {
                    "Completed" => $"interrogata, {source.Candidates} restituiti, " +
                        $"{source.NewUniqueCandidates} nuovi",
                    "NotRegistered" => "non registrata, non interrogata",
                    "Partial" => "ricerca con avvisi/errori; risultati non convalidati",
                    "PolicyRestricted" => "ricerca bloccata dai criteri Windows; disponibilità non verificata",
                    "NetworkError" => "errore di connessione/proxy/server; disponibilità non verificata",
                    _ => "ricerca non riuscita"
                };
                // Match the Home's strict WUA whitelist: an arbitrary
                // short IPC source may contain a PC name or server ID.
                var name = SafeLabel(source.Source, KnownDriverSources,
                    "Fonte Windows non riconosciuta");
                // Structured UTC pass metadata only: no raw COM text.
                var time = verifiedStatus && source.Outcome != "NotRegistered" &&
                    source.AttemptedAtUtc is { } attempted
                    ? " · tentata " + attempted.ToUniversalTime().ToString(
                        "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                    : string.Empty;
                output.AppendLine($"- {name}: {label}{time}");
            }
        }

        // Incomplete Type=Driver results are NOT driver offers. Copy only
        // counts, never WUA titles, GUIDs or hardware/device identifiers.
        if (report is { } partialReport &&
            (partialReport.PartialDriverReviewItems.Count > 0 ||
             partialReport.PartialDriverHintsOmitted > 0))
            output.AppendLine(
                "Indizi da ricerche driver WUA parziali (NON installabili): " +
                $"{partialReport.PartialDriverReviewItems.Count} visualizzati, " +
                $"{partialReport.PartialDriverHintsOmitted} oltre limite. " +
                "Non conteggiati negli aggiornamenti Windows confermati.");

        // Type='Software' results are informational only. Print aggregate
        // counts and known pass statuses, never software titles/device IDs.
        // A software failure does not make the independent driver query fail.
        if (report?.SoftwareSearchIsPartial == true)
            output.AppendLine("Verifica software OEM incompleta (solo informativa); " +
                "lo stato della ricerca driver Type=Driver resta indipendente.");

        if (report?.RelatedSoftwareSources is { Count: > 0 } softwareSources)
        {
            var confirmedSoftware = WuaDriverRelatedSoftwareClassifier
                .ConfirmedForReport(report);
            var verifiedSoftwarePass = WuaDriverRelatedSoftwareClassifier
                .HasCompletedKnownSoftwarePass(report);
            output.AppendLine(
                $"Componenti software produttore da verificare (NON driver): " +
                $"{confirmedSoftware.Count} visualizzati da fonti verificate, " +
                $"{(verifiedSoftwarePass ? report.RelatedSoftwareOmitted : 0)} oltre limite.");
            foreach (var source in softwareSources.Take(3))
            {
                var label = source.Succeeded &&
                    WuaDriverRelatedSoftwareClassifier.IsKnownSoftwareSource(
                        source.Source)
                    ? $"interrogata, {source.Candidates} Type=Software, " +
                      $"{source.NewUniqueCandidates} suggerimenti aggiunti"
                    : "fonte non verificata";
                var name = SafeLabel(source.Source, KnownSoftwareSources,
                    "Fonte software Windows non riconosciuta");
                output.AppendLine($"- {name}: {label}");
            }
        }

        output.AppendLine("Hardware riconosciuto per produttore:");
        // Strongly typed coverage report: ScopeNote is compiled-in text,
        // never an arbitrary IPC-supplied label or private source path.
        if (vendorCoverage is not null)
            output.AppendLine(vendorCoverage.ScopeNote);
        var listed = false;
        foreach (var row in vendorRows.Take(16))
        {
            listed = true;
            // A legacy/malformed IPC vendor is not trusted even if short
            // and path-free. Resolve the exact canonical vendor label.
            var vendor = SafeLabel(row.Vendor, KnownVendorLabels,
                "altro produttore");

            var anyDriverPassSucceeded =
                WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report);
            var budgetUnknown = !row.WuaCorrelationsComplete ||
                vendorCoverage is { CorrelationsComplete: false };
            var sourcePartial = report?.IsPartial == true ||
                vendorCoverage?.WuaSourcesPartial == true;
            // A copied summary must never turn an unqueried/failed
            // source or exhausted correlation budget into a false zero.
            var correlationText = !anyDriverPassSucceeded
                ? "ricerca WUA non verificata"
                : budgetUnknown
                    ? "correlazioni WUA non valutabili (limite di sicurezza)"
                    : sourcePartial
                        ? row.DevicesWithWuaMatches == 0
                            ? "nessun match nelle sole fonti WUA completate; " +
                              "ricerca PARZIALE, altre fonti non verificate"
                            : $"almeno {row.DevicesWithWuaMatches} con corrispondenze " +
                              "WUA per ID nelle sole fonti completate; " +
                              "ricerca PARZIALE, altre fonti non verificate"
                        : $"{row.DevicesWithWuaMatches} con corrispondenze WUA per ID";
            output.AppendLine(
                $"- {vendor}: {row.Devices} dispositivi; " +
                $"{correlationText}; " +
                $"{row.DevicesWithoutInstalledVersion} versioni locali mancanti");
        }

        if (!listed)
            output.AppendLine("- nessun produttore classificato");

        if (inventoriedDrivers is not null)
        {
            // The eight names are hard-coded diagnostic categories, not
            // copied PnP names or update titles. Even a failed inventory is
            // explicitly reported as unverified instead of a false zero.
            var reference = ReferenceDriverCoverage.Create(inventoriedDrivers, report);
            var driverQueried =
                WindowsDriverCandidateMerge.HasCompletedKnownWuaPass(report);
            var softwareQueried = WuaDriverRelatedSoftwareClassifier
                .HasCompletedKnownSoftwarePass(report);

            output.AppendLine("Confronto categorie Intel/Killer/NVIDIA (issue #91):");
            foreach (var row in reference)
                output.AppendLine("- " + row.DisplayLine(
                    inventoryComplete, driverQueried, softwareQueried,
                    driverPartial: report?.IsPartial == true,
                    softwarePartial: report?.SoftwareSearchIsPartial == true));
            output.AppendLine(
                "Zero dispositivi classificati non prova assenza del driver. " +
                "I titoli Type=Software non sono associati a dispositivi " +
                "e non entrano in Aggiorna tutto.");
        }

        output.AppendLine("Nota: i dispositivi riconosciuti e le fonti OEM non sono aggiornamenti confermati.");
        return output.ToString();
    }
}
