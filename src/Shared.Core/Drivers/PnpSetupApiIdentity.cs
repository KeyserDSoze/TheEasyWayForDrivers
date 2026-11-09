using System.Text;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Bounded, fail-closed decoder for read-only Windows SetupAPI
/// SPDRP_HARDWAREID / SPDRP_COMPATIBLEIDS (REG_MULTI_SZ, UTF-16 LE).
/// Values never leave local inventory and never authorize installation.
/// </summary>
public static class PnpSetupApiIdentity
{
    public const uint RegMultiSz = 7;
    public const int MaximumBytes = 64 * 1024;
    public const int MaximumIds = 128;
    private static readonly UnicodeEncoding StrictUtf16 =
        new(false, false, true);

    public sealed record Enrichment(
        IReadOnlyList<string> HardwareIds,
        IReadOnlyList<string> CompatibleIds,
        bool UsedSetupApi,
        bool UsedSignedDriver);

    public static IReadOnlyList<string> Decode(
        byte[]? buffer, uint registryType, uint validBytes)
    {
        if (buffer is null || registryType != RegMultiSz ||
            validBytes is < 4 or > MaximumBytes ||
            validBytes > buffer.Length ||
            (validBytes & 1) != 0)
            return [];

        var length = checked((int)validBytes);
        // A native REG_MULTI_SZ needs TWO final UTF-16 NUL characters.
        // Truncation must never turn an arbitrary fragment into an ID.
        if (buffer[length - 1] != 0 || buffer[length - 2] != 0 ||
            buffer[length - 3] != 0 || buffer[length - 4] != 0)
            return [];

        string decoded;
        try
        {
            decoded = StrictUtf16.GetString(buffer, 0, length);
        }
        catch (DecoderFallbackException)
        {
            return [];
        }

        if (decoded.Length < 2 ||
            decoded[^1] != '\0' || decoded[^2] != '\0')
            return [];

        var values = decoded[..^2].Split('\0');
        if (values.Length > MaximumIds ||
            values.Any(string.IsNullOrEmpty))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<string>(values.Length);
        foreach (var value in values)
        {
            if (!PnpIdentifierFallback.IsStrictSingleId(value))
                continue;
            if (seen.Add(value))
                found.Add(value);
        }
        return found;
    }

    /// <summary>
    /// Normalize the Win32_PnPEntity HardwareID / CompatibleID REG_MULTI_SZ
    /// values BEFORE checking whether SetupAPI native fallback is needed.
    /// A nonempty WMI array with only malformed entries is NOT evidence of
    /// usable identity. Never turn a PnP instance path or user path into a
    /// hardware ID. Over-budget arrays are rejected as a whole rather than
    /// silently marking a truncated prefix as exhaustive.
    /// </summary>
    public static IReadOnlyList<string> SanitizeWmiIds(
        IReadOnlyList<string> rawIds)
    {
        ArgumentNullException.ThrowIfNull(rawIds);
        if (rawIds.Count > MaximumIds)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>(rawIds.Count);
        foreach (var id in rawIds)
        {
            if (PnpIdentifierFallback.IsStrictSingleId(id) && seen.Add(id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// A nonempty WMI HardwareID list can still identify only the PCI
    /// class or vendor, without the VEN+DEV model required for device
    /// correlation. Recover a model only from the SAME native PnP instance.
    /// A complete WMI hardware model or another bus always wins.
    /// Multiple conflicting VEN tokens are ambiguous and fail closed.
    /// </summary>
    public static bool NeedsPciHardwareModelRecovery(
        IReadOnlyList<string> wmiHardware)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        var valid = SanitizeWmiIds(wmiHardware);
        if (valid.Count == 0)
            return false; // ordinary missing-ID path already handles this

        string? vendor = null;
        foreach (var id in valid)
        {
            if (!TryIncompletePciIdentity(id, out var currentVendor))
                return false;
            if (currentVendor is null)
                continue;
            if (vendor is not null &&
                !vendor.Equals(currentVendor, StringComparison.OrdinalIgnoreCase))
                return false;
            vendor = currentVendor;
        }
        return true;
    }

    // Only these *exact* PCI class/vendor-only token shapes qualify.
    // Malformed, DEV-bearing, instance/path and non-PCI IDs cannot trigger
    // an override of Windows' authoritative HardwareID list.
    private static bool TryIncompletePciIdentity(string id, out string? vendor)
    {
        vendor = null;
        const string classPrefix = @"PCI\CC_";
        if (id.StartsWith(classPrefix, StringComparison.OrdinalIgnoreCase))
            return IsClassCode(id[classPrefix.Length..]);

        const string vendorPrefix = @"PCI\VEN_";
        if (!id.StartsWith(vendorPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = id[vendorPrefix.Length..];
        if (rest.Length < 4 || !rest[..4].All(Uri.IsHexDigit))
            return false;
        if (rest.Length > 4)
        {
            const string classSuffix = "&CC_";
            if (!rest[4..].StartsWith(classSuffix,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsClassCode(rest[(4 + classSuffix.Length)..]))
                return false;
        }

        vendor = rest[..4];
        return true;
    }

    private static bool IsClassCode(string value) =>
        value.Length is 4 or 6 && value.All(Uri.IsHexDigit);

    private static bool IsValidatedPciModel(string? id) =>
        PnpIdentifierFallback.IsStrictSingleId(id) &&
        MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id)?
            .StartsWith(@"PCI\VEN_", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Strict priority: complete Win32_PnPEntity model IDs win. Only
    /// missing identity or incomplete PCI class/vendor-only tokens may use
    /// same-instance native SetupAPI; require validated VEN+DEV and preserve
    /// the earlier WMI tokens behind the recovered hardware model.
    /// No native fallback may borrow another device's vendor/identity.
    /// </summary>
    public static Enrichment Merge(
        IReadOnlyList<string> wmiHardware,
        IReadOnlyList<string> wmiCompatible,
        IReadOnlyList<string> nativeHardware,
        IReadOnlyList<string> nativeCompatible,
        string? signedHardware,
        string? signedCompatible)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        ArgumentNullException.ThrowIfNull(wmiCompatible);
        ArgumentNullException.ThrowIfNull(nativeHardware);
        ArgumentNullException.ThrowIfNull(nativeCompatible);

        var validWmiHardware = SanitizeWmiIds(wmiHardware);
        var validWmiCompatible = SanitizeWmiIds(wmiCompatible);
        var incompletePci = NeedsPciHardwareModelRecovery(validWmiHardware);
        // Any WMI VEN evidence constrains recovery to THAT vendor only.
        // This prevents a foreign native ID from silently relabeling the
        // device when WMI reports PCI\\VEN_10DE or VEN_8086&CC_0280.
        var wmiVendor = incompletePci
            ? validWmiHardware.Select(id =>
                TryIncompletePciIdentity(id, out var vendor) ? vendor : null)
                .FirstOrDefault(vendor => vendor is not null)
            : null;
        var nativeHw = validWmiHardware.Count == 0 || incompletePci
            ? nativeHardware.Where(id => incompletePci
                    ? IsValidatedPciModel(id) &&
                      (wmiVendor is null || id!.StartsWith(
                          @"PCI\VEN_" + wmiVendor + "&DEV_",
                          StringComparison.OrdinalIgnoreCase))
                    : PnpIdentifierFallback.IsStrictSingleId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];
        var nativeComp = validWmiCompatible.Count == 0
            ? nativeCompatible.Where(PnpIdentifierFallback.IsStrictSingleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];

        // Class/vendor-only tokens are NOT authoritative hardware models.
        // Keep original WMI evidence but prefer validated same-instance PCI
        // VEN+DEV for exact matching and vendor discovery; if no native
        // model is available, retain WMI identity unchanged.
        var preferredHardware = incompletePci && nativeHw.Length > 0
            ? nativeHw.Concat(validWmiHardware)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : validWmiHardware.Count > 0 ? validWmiHardware : nativeHw;
        var preferredCompatible = validWmiCompatible.Count > 0
            ? validWmiCompatible : nativeComp;
        var result = PnpIdentifierFallback.Combine(
            preferredHardware, preferredCompatible,
            signedHardware, signedCompatible);

        return new Enrichment(result.HardwareIds, result.CompatibleIds,
            nativeHw.Length > 0 || nativeComp.Length > 0,
            result.UsedSignedDriverFallback);
    }
}
