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

    public sealed record Result(string Message, IReadOnlyList<VendorSourceCheck> Rows);

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
            if (manufacturer.Contains("Dell", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(model) || model.Length > 160)
                    return new Result("Dell: modello non identificato; catalogo non interrogato.", []);

                var packs = await new DellCatalogReader().FindAsync(model, timeout.Token);
                var rows = packs.Take(12).Select(pack =>
                    new VendorSourceCheck(
                        $"Dell Driver Pack (deployment) · {pack.Model}",
                        "Dell", "N/D", DellHelp, "Dell Driver Pack Catalog",
                        $"Sistemi operativi: {pack.OperatingSystems}; non è un singolo driver",
                        0, true, pack.Release)).ToArray();

                return new Result(
                    $"Dell Driver Pack Catalog: interrogato per modello '{model}', " +
                    $"{packs.Count} pacchetti deployment pubblicati. " +
                    "Non prova compatibilità per singolo dispositivo.",
                    rows);
            }

            if (manufacturer.Equals("HP", StringComparison.OrdinalIgnoreCase) ||
                manufacturer.Contains("Hewlett", StringComparison.OrdinalIgnoreCase))
            {
                var response = await HpSoftpaqCatalogReader.QueryCurrentPcAsync(timeout.Token);
                var packages = HpSoftpaqCatalogParser.Parse(response);
                var rows = packages.Take(20).Select(package =>
                    new VendorSourceCheck(
                        $"HP SoftPaq · {package.Title}", "HP", "N/D",
                        HpHelp, "HP Client Management Script Library",
                        $"SoftPaq {package.Id} · {package.ReleaseType}; verifica piattaforma e ID",
                        0, true, package.Version)).ToArray();

                return new Result(
                    $"HP CMSL: catalogo ufficiale interrogato, " +
                    $"{packages.Count} metadati SoftPaq Driver (non installazioni approvate).",
                    rows);
            }

            if (manufacturer.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
                return new Result(
                    "Lenovo: richiede MachineType a 4 caratteri verificato e modulo " +
                    "Lenovo.Client.Scripting. Ricerca ufficiale disponibile in Dettagli avanzati.",
                    []);

            return new Result(
                $"OEM '{manufacturer}': catalogo di pacchetti non interrogato. " +
                "Consulta la fonte ufficiale per il modello esatto, in Dettagli avanzati.",
                []);
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
                $"Catalogo OEM {manufacturer}: non verificabile in questa scansione " +
                "(rete, timeout o modulo ufficiale non disponibile). " +
                "Non significa che non esistano aggiornamenti.",
                []);
        }
    }
}
