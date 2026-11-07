namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Read-only, per-device PnP identity enrichment. Win32_PnPEntity is
/// authoritative and Win32_PnPSignedDriver can provide one HardwareID
/// and/or CompatID only where the corresponding PnP list is empty.
/// This evidence is for correlation, NOT proof of a newer driver.
/// </summary>
public static class PnpIdentifierFallback
{
    public const int MaxIdentifierLength = 512;

    public sealed record Result(
        IReadOnlyList<string> HardwareIds,
        IReadOnlyList<string> CompatibleIds,
        bool UsedSignedDriverFallback);

    public static Result Combine(
        IReadOnlyList<string> pnpHardwareIds,
        IReadOnlyList<string> pnpCompatibleIds,
        string? signedDriverHardwareId,
        string? signedDriverCompatibleId)
    {
        ArgumentNullException.ThrowIfNull(pnpHardwareIds);
        ArgumentNullException.ThrowIfNull(pnpCompatibleIds);

        // Never change WMI PnP priority or quietly append weaker signed
        // identifiers when Windows already reported that identity category.
        var hardware = pnpHardwareIds;
        var compatible = pnpCompatibleIds;
        var recovered = false;

        if (hardware.Count == 0 &&
            TrySingleId(signedDriverHardwareId, out var candidateHardware))
        {
            hardware = [candidateHardware];
            recovered = true;
        }

        if (compatible.Count == 0 &&
            TrySingleId(signedDriverCompatibleId, out var candidateCompatible) &&
            !hardware.Any(id => string.Equals(id, candidateCompatible,
                StringComparison.OrdinalIgnoreCase)))
        {
            compatible = [candidateCompatible];
            recovered = true;
        }

        return new Result(hardware, compatible, recovered);
    }

    /// <summary>
    /// Strict single PnP bus-qualified identifier, without instance paths,
    /// whitespace, controls, command syntax or ambiguous Unicode. Used by
    /// the native SetupAPI REG_MULTI_SZ fallback and existing SignedDriver
    /// fallback; never grants driver installation permission.
    /// </summary>
    public static bool IsStrictSingleId(string? raw) =>
        raw is not null &&
        raw.Equals(raw.Trim(), StringComparison.Ordinal) &&
        TrySingleId(raw, out _);

    private static bool TrySingleId(string? raw, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Reject CR/LF/NUL BEFORE trimming. Trimming a malformed raw WMI
        // value must never turn it into a seemingly valid PnP identifier.
        if (raw.Any(char.IsControl))
            return false;

        var value = raw.Trim();
        if (value.Length is < 3 or > MaxIdentifierLength ||
            value.Contains("..", StringComparison.Ordinal))
            return false;

        var delimiter = value.IndexOf('\\');
        if (delimiter is < 1 or > 31 || delimiter == value.Length - 1)
            return false;

        // A single bus-qualified PnP token, not an arbitrary string/path,
        // multiple concatenated IDs, or an executable command argument.
        if (!value[..delimiter].All(c =>
                char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            return false;

        if (!value[(delimiter + 1)..].All(c =>
                char.IsAsciiLetterOrDigit(c) ||
                c is '_' or '&' or '#' or '{' or '}' or '+' or '-' or '.'))
            return false;

        id = value;
        return true;
    }
}
