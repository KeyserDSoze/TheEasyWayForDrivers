using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>Separates Windows PnP faults from missing signed-driver metadata.</summary>
public static class DriverAttentionFilter
{
    public static bool Matches(DriverInfo driver, bool hasAvailableUpdate, string? filter)
    {
        ArgumentNullException.ThrowIfNull(driver);

        return filter?.ToLowerInvariant() switch
        {
            null or "" or "all" => true,
            "hardware-problems" => driver.HasWindowsProblem,
            "attention" => driver.NeedsAttention || hasAvailableUpdate,
            _ => string.Equals(
                driver.NeedsAttention ? driver.Status :
                hasAvailableUpdate ? "Aggiornamento disponibile" : "OK",
                filter, StringComparison.OrdinalIgnoreCase)
        };
    }
}
