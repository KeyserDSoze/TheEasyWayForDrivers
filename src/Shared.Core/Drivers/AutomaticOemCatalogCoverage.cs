namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// An optional HP CMSL / Dell Driver Pack query has its own outcome:
/// OEM provider detection may succeed while catalog metadata cannot be
/// verified. This independent source must not poison or erase WUA passes.
/// </summary>
public static class AutomaticOemCatalogCoverage
{
    // A stable, anonymous label: never copy OEM name, system model, source
    // URLs, serial numbers, HRESULT details or package names to the scan
    // summary's unavailable-step list.
    public const string UnavailableStep = "catalogo OEM ufficiale";

    /// <summary>
    /// State of the automatic Dell Driver Pack / HP CMSL METADATA lookup,
    /// not an OEM applicability scan. Never inferred from Rows.Count or
    /// displayed source text. This carries no hardware identifiers.
    /// </summary>
    public enum CatalogQueryStatus
    {
        NotApplicable,
        Completed,
        MissingPrerequisites,
        Failed
    }

    public static CatalogQueryStatus Classify(
        bool wasQueried, bool isVerificationIncomplete, bool canRetry)
    {
        // Failed attempts and missing prerequisites must take precedence
        // over arbitrary counts in legacy/partially populated results.
        if (isVerificationIncomplete)
            return canRetry ? CatalogQueryStatus.Failed :
                CatalogQueryStatus.MissingPrerequisites;
        // An inconsistent "retry" flag without an incomplete outcome
        // cannot be interpreted as a confirmed successful catalog query.
        if (canRetry)
            return CatalogQueryStatus.Failed;
        return wasQueried ? CatalogQueryStatus.Completed :
            CatalogQueryStatus.NotApplicable;
    }

    /// <summary>
    /// Copyable, aggregate, non-identifying status. No OEM model/serial,
    /// network endpoint, local path, PowerShell error or package title
    /// can enter the report through this strongly typed enum.
    /// </summary>
    public static string StatusForAnonymousReport(CatalogQueryStatus status) =>
        status switch
        {
            CatalogQueryStatus.NotApplicable =>
                "Catalogo OEM automatico Dell/HP: non interrogato / non applicabile; " +
                "nessun controllo di pacchetti OEM deducibile.",
            CatalogQueryStatus.Completed =>
                "Catalogo OEM automatico Dell/HP: interrogato; solo metadati " +
                "pubblicati, NON prova driver necessari, versioni più recenti " +
                "o installabilità (anche con zero risultati).",
            CatalogQueryStatus.MissingPrerequisites =>
                "Catalogo OEM automatico Dell/HP: non interrogato, " +
                "prerequisiti o modello PC non verificabili; NON significa " +
                "zero driver OEM.",
            CatalogQueryStatus.Failed =>
                "Catalogo OEM automatico Dell/HP: query fallita/non " +
                "verificata (rete, timeout o modulo ufficiale); " +
                "NON significa zero driver OEM.",
            _ =>
                "Catalogo OEM automatico Dell/HP: stato non riconosciuto, " +
                "verifica NON disponibile."
        };

    /// <summary>
    /// Replace ONLY the automated OEM catalog's coverage status after an
    /// explicit retry, without losing any independent WUA/inventory/other
    /// source failure. A successful zero-row query is *complete* metadata
    /// inspection, not proof that all hardware drivers are up to date.
    /// </summary>
    public static IReadOnlyCollection<string> WithOutcome(
        IEnumerable<string> unavailableSteps, bool isVerificationIncomplete)
    {
        ArgumentNullException.ThrowIfNull(unavailableSteps);
        var remaining = unavailableSteps
            .Where(step => step != UnavailableStep)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (isVerificationIncomplete)
            remaining.Add(UnavailableStep);
        return remaining;
    }
}
