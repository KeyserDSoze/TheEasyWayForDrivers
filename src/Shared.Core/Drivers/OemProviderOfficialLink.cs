using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Browser navigation gate for the four OEM provider IDs exposed by
/// the Desktop/Service IPC. A valid HTTPS scheme is NOT proof of vendor
/// ownership. Reconstruct a fixed canonical URL locally and never open
/// a URL supplied by an IPC payload without an exact match.
/// </summary>
public static class OemProviderOfficialLink
{
    // Keep this independent of the vendor-news / per-device handoff:
    // the OEM provider tab uses the official NVIDIA App drivers landing.
    public const string NvidiaDriversUrl =
        "https://www.nvidia.com/en-us/geforce/drivers/";

    /// <summary>
    /// Returns the trusted, locally reconstructed URL. It must not
    /// return the potentially untrusted OfficialSupportUrl from IPC.
    /// An unknown/inapplicable provider has no allowed destination.
    /// </summary>
    public static bool TryGetOfficialUrl(
        OemProviderStatus? provider, out string officialUrl)
    {
        officialUrl = string.Empty;
        if (provider is null || !provider.IsApplicable)
            return false;

        var canonicalUrl = provider.ProviderId?.ToLowerInvariant() switch
        {
            "intel" => VendorSourceDiscovery.IntelDsaUrl,
            "nvidia" => NvidiaDriversUrl,
            "amd" => VendorSourceDiscovery.AmdDriversUrl,
            "system-oem" => SystemOemCatalog.Match(
                provider.SystemManufacturer, provider.SystemModel)
                ?.OfficialSupportUrl,
            _ => null
        };

        // Exact literal comparison rejects arbitrary HTTPS hosts,
        // lookalikes, userinfo, ports, queries, fragments and redirects.
        // All canonical URLs are defined in the signed application code.
        if (canonicalUrl is null ||
            !string.Equals(provider.OfficialSupportUrl, canonicalUrl,
                StringComparison.Ordinal))
            return false;

        officialUrl = canonicalUrl;
        return true;
    }
}
