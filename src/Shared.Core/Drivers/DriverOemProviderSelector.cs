using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Selects an existing official OEM handoff; this does not check whether a
/// compatible driver exists at the destination.
/// </summary>
public static class DriverOemProviderSelector
{
    public static OemProviderStatus? Choose(
        string? recommendedSource,
        IEnumerable<OemProviderStatus> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var available = providers
            .Where(provider => provider.IsApplicable &&
                Uri.TryCreate(provider.OfficialSupportUrl, UriKind.Absolute, out var url) &&
                url.Scheme == Uri.UriSchemeHttps)
            .ToArray();

        var preferredVendor = recommendedSource switch
        {
            { } source when source.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase) => "nvidia",
            { } source when source.StartsWith("AMD", StringComparison.OrdinalIgnoreCase) => "amd",
            { } source when source.StartsWith("Intel", StringComparison.OrdinalIgnoreCase) => "intel",
            _ => null
        };

        if (preferredVendor is not null)
        {
            var vendor = available.FirstOrDefault(provider =>
                string.Equals(provider.ProviderId, preferredVendor, StringComparison.OrdinalIgnoreCase));

            if (vendor is not null)
                return vendor;
        }

        return available.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, "system-oem", StringComparison.OrdinalIgnoreCase));
    }
}
