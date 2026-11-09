using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only PnP identity for vendor DCH software/extension components.
/// A valid SWC bus+vendor+model token is NOT a vendor catalog result,
/// an available driver or proof that Windows will install anything.
/// </summary>
public static class SwcVendorComponentIdentity
{
    public static bool IsIntel(DriverInfo driver) =>
        string.Equals(RecognizedVendor(driver), "8086", StringComparison.Ordinal);

    public static bool IsRealtek(DriverInfo driver) =>
        string.Equals(RecognizedVendor(driver), "10EC", StringComparison.Ordinal);

    private static string? RecognizedVendor(DriverInfo driver)
    {
        ArgumentNullException.ThrowIfNull(driver);

        // A missing DCH driver may leave PNPClass empty, Unknown, Other or
        // System even while Windows provides a genuine SWC VEN+DEV model.
        // Explicit, unrelated device classes must never be rebranded as SWC.
        var deviceClass = driver.DeviceClass?.Trim();
        if (!string.IsNullOrEmpty(deviceClass) &&
            !new[] { "SoftwareComponent", "Extension", "Unknown",
                "System", "Other" }.Contains(deviceClass,
                    StringComparer.OrdinalIgnoreCase))
            return null;

        // A primary HardwareID is stronger than a CompatibleID. If two
        // primary SWC models declare conflicting vendors, fail closed
        // instead of choosing whichever entry happened to appear first.
        var hardwareVendors = ReadVendors(driver.HardwareIds);
        if (hardwareVendors.Count > 0)
            return hardwareVendors.Count == 1 ? hardwareVendors.Single() : null;

        var compatibleVendors = ReadVendors(driver.CompatibleIds);
        return compatibleVendors.Count == 1 ? compatibleVendors.Single() : null;
    }

    private static IReadOnlySet<string> ReadVendors(IEnumerable<string> ids)
    {
        var vendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ids)
        {
            // The shared parser enforces exactly one PnP bus separator,
            // 4-digit VEN/DEV and only documented SUBSYS/REV suffixes.
            // Prefix-only checks accepted SWC\\VEN_8086&DEV_1234&FAKE_XX
            // and instance-like strings as vendor evidence.
            var publicModel = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(raw);
            const string prefix = @"SWC\VEN_";
            if (publicModel is null ||
                !publicModel.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            vendors.Add(publicModel.Substring(prefix.Length, 4));
        }
        return vendors;
    }
}
