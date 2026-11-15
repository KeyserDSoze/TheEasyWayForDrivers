namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Fixed, non-identifying status for read-only automatic OEM metadata probes.
/// Raw WMI model/manufacturer values must never be interpolated into Home.
/// </summary>
public static class AutomaticOemCatalogDisplayText
{
    public static string DellCompleted(int publishedPacks) =>
        $"Dell Driver Pack Catalog: interrogato per il modello locale; {Math.Max(0, publishedPacks)} " +
        "pacchetti deployment pubblicati. Per verificare driver effettivamente necessari " +
        "usa Dell Command Update (se gia installato). Un Driver Pack non prova " +
        "applicabilita a una singola periferica.";

    public static string HpCompleted(int publishedSoftpaqs) =>
        $"HP CMSL: catalogo ufficiale interrogato; {Math.Max(0, publishedSoftpaqs)} " +
        "metadati SoftPaq Driver, non necessariamente aggiornamenti necessari. " +
        "Per le raccomandazioni del PC corrente usa HP Image Assistant (se gia installato).";

    public const string UnknownProducer =
        "Produttore del PC non associato a un catalogo automatico Dell/HP. " +
        "Consulta la fonte ufficiale in Dettagli avanzati.";

    public const string QueryFailed =
        "Catalogo OEM ufficiale non verificabile in questa scansione " +
        "(rete, timeout o modulo non disponibile). Non significa che non esistano aggiornamenti.";
}
