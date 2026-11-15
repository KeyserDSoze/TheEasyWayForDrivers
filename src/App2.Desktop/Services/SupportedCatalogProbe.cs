using System.IO;
using System.Net.Http;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Queries officially published OEM metadata only when the actual PC vendor
/// can be identified. No executable, driver or firmware is downloaded/started.
/// Dell provides deployment driver packs; HP CMSL provides SoftPaq metadata
/// only if the official local module is already installed.
/// </summary>
public static class SupportedCatalogProbe
{
    private const string DellHelp = "https://www.dell.com/support/home";
    private const string HpHelp = "https://support.hp.com/";

    // #470: the OEM provider IPC discovery can succeed while a separate
    // optional Dell/HP catalog lookup fails. Do not equate empty Rows with
    // success, nor infer query outcome from a localized Message. A retry
    // is useful only after a temporary query failure, not when Dell's
    // system model cannot be identified.
    public sealed record Result(
        string Message, IReadOnlyList<VendorSourceCheck> Rows,
        bool IsVerificationIncomplete = false, bool CanRetry = false,
        bool WasQueried = false)
    {
        // #471: success vs not-attempted is independent of Rows.Count.
        // This value is the ONLY catalog status exported to QA, never
        // an OEM system model, SoftPaq title or exception message.
        public AutomaticOemCatalogCoverage.CatalogQueryStatus AuditStatus =>
            AutomaticOemCatalogCoverage.Classify(
                WasQueried, IsVerificationIncomplete, CanRetry);
    }

    public static async Task<Result> TryReadAsync(
        string? manufacturer, string? model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(manufacturer))
            return new Result("OEM PC: produttore non identificato; nessun catalogo interrogato.", []);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));

        try
        {
            if (SystemOemCatalog.IsDellManufacturer(manufacturer))
            {
                if (string.IsNullOrWhiteSpace(model) || model.Length > 160)
                    return new Result(
                        "Dell: modello non identificato; catalogo non interrogato. " +
                        "Nessuna assenza di aggiornamenti può essere verificata.",
                        [], IsVerificationIncomplete: true);

                var packs = await new DellCatalogReader().FindAsync(model, timeout.Token);
                var rows = packs.Take(12).Select(pack =>
                    new VendorSourceCheck(
                        $"Dell Driver Pack (deployment) · {pack.Model}",
                        "Dell", "N/D", DellHelp, "Dell Driver Pack Catalog",
                        $"Sistemi operativi: {pack.OperatingSystems}; non è un singolo driver",
                        0, true, pack.Release)).ToArray();

                return new Result(
                    AutomaticOemCatalogDisplayText.DellCompleted(packs.Count),
                    rows, WasQueried: true);
            }

            if (SystemOemCatalog.IsHpManufacturer(manufacturer))
            {
                var response = await HpSoftpaqCatalogReader.QueryCurrentPcAsync(timeout.Token);
                var packages = HpSoftpaqCatalogParser.Parse(response);
                var rows = packages.Take(60).Select(package =>
                    new VendorSourceCheck(
                        $"HP SoftPaq · {package.Title}", "HP", "N/D",
                        HpHelp, "HP Client Management Script Library",
                        $"SoftPaq {package.Id} · {package.ReleaseType}; verifica piattaforma e ID",
                        0, true, package.Version)).ToArray();

                return new Result(
                    AutomaticOemCatalogDisplayText.HpCompleted(packages.Count),
                    rows, WasQueried: true);
            }

            if (SystemOemCatalog.IsPcSpecialistManufacturer(manufacturer))
                return new Result(
                    "PCSpecialist: sito ufficiale con driver di fabbrica per la " +
                    "configurazione acquistata. Premi «Driver PCSpecialist» e " +
                    "inserisci il numero d'ordine solo sul sito del produttore. " +
                    "OmegaDrive non legge il numero, non ha interrogato il catalogo " +
                    "per il tuo ordine e non conferma versioni più recenti.",
                    []);

            if (SystemOemCatalog.IsLenovoManufacturer(manufacturer))
                return new Result(
                    "Lenovo: la verifica degli aggiornamenti necessari è disponibile " +
                    "dal pulsante dedicato dopo l'analisi, con Lenovo.Client.Update già installato. " +
                    "Il catalogo Lenovo.Client.Scripting per MachineType rimane in Dettagli avanzati.",
                    []);

            return new Result(
                AutomaticOemCatalogDisplayText.UnknownProducer, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or HttpRequestException or
            UnauthorizedAccessException or InvalidDataException or InvalidOperationException or
            System.Xml.XmlException or System.Text.Json.JsonException or
            System.ComponentModel.Win32Exception)
        {
            return new Result(
                AutomaticOemCatalogDisplayText.QueryFailed,
                [], IsVerificationIncomplete: true, CanRetry: true);
        }
    }
}
