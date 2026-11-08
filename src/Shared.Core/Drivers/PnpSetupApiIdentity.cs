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
    /// Strict priority: Win32_PnPEntity lists win. For only missing
    /// categories, SetupAPI data wins over the weaker single
    /// Win32_PnPSignedDriver value. Never join by name or manufacturer.
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
        var nativeHw = validWmiHardware.Count == 0
            ? nativeHardware.Where(PnpIdentifierFallback.IsStrictSingleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];
        var nativeComp = validWmiCompatible.Count == 0
            ? nativeCompatible.Where(PnpIdentifierFallback.IsStrictSingleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaximumIds).ToArray()
            : [];

        var preferredHardware = validWmiHardware.Count > 0
            ? validWmiHardware : nativeHw;
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
