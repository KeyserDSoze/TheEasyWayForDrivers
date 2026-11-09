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
    /// Win32_PnPEntity occasionally reports only PCI class codes as
    /// HardwareIDs, e.g. PCI\\CC_030000 for a generic display device.
    /// These identify a device TYPE, not its VEN+DEV model. Only this
    /// narrowly defined class-only case may trigger an exact-instance
    /// SetupAPI PCI hardware-model recovery despite a nonempty WMI list.
    /// A complete WMI model (or a different bus) remains authoritative.
    /// </summary>
    public static bool NeedsPciHardwareModelRecovery(
        IReadOnlyList<string> wmiHardware)
    {
        ArgumentNullException.ThrowIfNull(wmiHardware);
        var valid = SanitizeWmiIds(wmiHardware);
        if (valid.Count == 0)
            return false; // ordinary missing-ID path already handles this

        const string prefix = @"PCI\CC_";
        return valid.All(id =>
        {
            if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            var code = id[prefix.Length..];
            return code.Length is 4 or 6 && code.All(Uri.IsHexDigit);
        });
    }

    private static bool IsValidatedPciModel(string? id) =>
        PnpIdentifierFallback.IsStrictSingleId(id) &&
        MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id)?
            .StartsWith(@"PCI\VEN_", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Strict priority: complete Win32_PnPEntity model IDs win. Only
    /// missing identity or class-only PCI IDs may use same-instance native
    /// SetupAPI; for class-only WMI, require a validated native VEN+DEV
    /// model and preserve the earlier class tokens behind native hardware.
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
        var classOnlyPci = NeedsPciHardwareModelRecovery(validWmiHardware);
        var nativeHw = validWmiHardware.Count == 0 || classOnlyPci
            ? nativeHardware.Where(id => classOnlyPci
                    ? IsValidatedPciModel(id)
                    : PnpIdentifierFallback.IsStrictSingleId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];
        var nativeComp = validWmiCompatible.Count == 0
            ? nativeCompatible.Where(PnpIdentifierFallback.IsStrictSingleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];

        // A class code alone is NOT an authoritative hardware model.
        // Keep the original WMI evidence, but prefer validated same-instance
        // native PCI models for exact matching and vendor discovery.
        // If native recovery is unavailable, use WMI as before.
        var preferredHardware = classOnlyPci && nativeHw.Length > 0
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
