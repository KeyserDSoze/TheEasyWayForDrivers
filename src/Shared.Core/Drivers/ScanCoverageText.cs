using System.Text;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Human-readable, deliberately non-identifying evidence for support/QA.
/// Do not include raw device instance IDs, Hardware IDs, serial numbers,
/// computer name, installed package paths or model identifiers.
/// </summary>
public static class ScanCoverageText
{
    public static string Create(
        DriverSearchReport? report,
        IEnumerable<VendorCoverageRow> vendorRows,
        int deviceCount,
        int problemCount,
        int manufacturerRecommended,
        IReadOnlyList<DriverInfo>? inventoriedDrivers = null,
        bool inventoryComplete = false)
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
            // Aggregate only: never export the recovered PnP identity itself.
            var identityRecovered = inventoriedDrivers.Count(
                device => device.PnpIdsRecoveredFromSignedDriver);
            if (identityRecovered > 0)
                output.AppendLine(
                    $"Identificativi PnP integrati da metadati driver locali: " +
                    $"{identityRecovered} dispositivi. " +
                    "Migliora la correlazione WUA per ID, NON conferma nuovi aggiornamenti.");
        }
        output.AppendLine(report is null || report.AllFailed
            ? "Windows Update: ricerca non verificata"
            : $"Windows Update: {report.Updates.Count} candidati distinti");

        output.AppendLine("Fonti WUA:");
        if (report is null || report.Sources.Count == 0)
        {
            output.AppendLine("- nessuna fonte verificata");
        }
        else
        {
            foreach (var source in report.Sources.Take(12))
            {
                var label = source.Outcome switch
                {
                    "Completed" => $"interrogata, {source.Candidates} restituiti, " +
                        $"{source.NewUniqueCandidates} nuovi",
                    "NotRegistered" => "non registrata, non interrogata",
                    "Partial" => "ricerca con avvisi/errori; risultati non convalidati",
                    _ => "ricerca non riuscita"
                };
                // Known source names emitted by OmegaDrive, never filesystem
                // paths or arbitrary error details.
                var name = source.Source;
                if (name.Length > 70 || name.Any(c => char.IsControl(c) ||
                    c is '/' or '\\' or ':'))
                    name = "fonte Windows";
                output.AppendLine($"- {name}: {label}");
            }
        }

        // Type='Software' results are informational only. Print aggregate
        // counts and known pass statuses, never software titles/device IDs.
        // A software failure does not make the independent driver query fail.
        if (report?.SoftwareSearchIsPartial == true)
            output.AppendLine("Verifica software OEM incompleta (solo informativa); " +
                "lo stato della ricerca driver Type=Driver resta indipendente.");

        if (report?.RelatedSoftwareSources is { Count: > 0 } softwareSources)
        {
            output.AppendLine(
                $"Componenti software produttore da verificare (NON driver): " +
                $"{report.SoftwareReviewItems.Count} visualizzati, " +
                $"{report.RelatedSoftwareOmitted} oltre limite.");
            foreach (var source in softwareSources.Take(3))
            {
                var label = source.Succeeded
                    ? $"interrogata, {source.Candidates} Type=Software, " +
                      $"{source.NewUniqueCandidates} suggerimenti aggiunti"
                    : "fonte non verificata";
                var name = source.Source;
                if (name.Length > 70 || name.Any(c => char.IsControl(c) ||
                    c is '/' or '\\' or ':'))
                    name = "fonte software Windows";
                output.AppendLine($"- {name}: {label}");
            }
        }

        output.AppendLine("Hardware riconosciuto per produttore:");
        var listed = false;
        foreach (var row in vendorRows.Take(16))
        {
            listed = true;
            var vendor = row.Vendor;
            // Vendor must be one of the names from the trusted classifier;
            // do not echo arbitrary PnP names or other unique identifiers.
            if (vendor.Length > 35 || vendor.Any(c =>
                char.IsControl(c) || c is '/' or '\\' or ':'))
                vendor = "altro produttore";

            output.AppendLine(
                $"- {vendor}: {row.Devices} dispositivi; " +
                $"{row.DevicesWithWuaMatches} con corrispondenze WUA per ID; " +
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
            var driverQueried = report is { AllFailed: false };
            var softwareQueried = report?.RelatedSoftwareSources?.Any(
                source => source.Succeeded) == true;

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
