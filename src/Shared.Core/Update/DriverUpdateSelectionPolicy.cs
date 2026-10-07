using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

public static class DriverUpdateSelectionPolicy
{
    public static bool ShouldSelectByDefault(
        DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return !update.IsAdvancedCandidate &&
               !update.IsHidden;
    }
}
