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
            if (update.IsHidden ||
                update.IsAdvancedCandidate)
            {
                advanced++;
                continue;
            }

            if (update.IsOptional)
            {
                optional++;
                continue;
            }

            recommended++;
        }

        return new DriverSearchSummary(
            recommended,
            optional,
            advanced,
            Math.Max(oemSources, 0));
    }
}
