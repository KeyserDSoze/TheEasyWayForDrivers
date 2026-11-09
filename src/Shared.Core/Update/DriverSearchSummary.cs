using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

public sealed record DriverSearchSummary(
    int Recommended,
    int Optional,
    int Advanced,
    int OemSources)
{
    public static DriverSearchSummary Create(
        IEnumerable<DriverUpdateInfo> updates,
        int oemSources)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var recommended = 0;
        var optional = 0;
        var advanced = 0;

        foreach (var update in updates)
        {
            // The Home "Consigliati" badge MUST count the same GUIDs
            // as the actual one-click/update-all eligibility gate.
            // Hidden, firmware, malformed GUID or unknown-origin entries
            // may exist as WUA/OEM review evidence, never as installable
            // recommended offers.
            if (QuickUpdatePolicy.IsEligible(update))
                recommended++;
            else if (CompactResultFilter.IsOptionalReview(update))
                optional++;
            else
                advanced++;
        }

        return new DriverSearchSummary(
            recommended,
            optional,
            advanced,
            Math.Max(oemSources, 0));
    }
}
