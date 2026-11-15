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
