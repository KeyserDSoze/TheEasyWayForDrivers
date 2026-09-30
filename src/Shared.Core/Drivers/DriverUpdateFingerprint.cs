using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class DriverUpdateFingerprint
{
    public static string Create(
        IEnumerable<DriverUpdateInfo> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        return string.Join(
            "|",
            updates
                .Select(update => update.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    id => id,
                    StringComparer.OrdinalIgnoreCase));
    }
}
