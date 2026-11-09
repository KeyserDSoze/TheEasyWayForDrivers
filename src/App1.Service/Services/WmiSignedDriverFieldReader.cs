using System.Management;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Decode only optional, per-row Win32_PnPSignedDriver fields.
/// A malformed WMI DMTF DriverDate is unknown: it must not discard the
/// installed version, provider or INF name, nor abort Win32_PnPEntity.
/// Query/enumeration failures remain handled by WmiOptionalMetadataQuery.
/// </summary>
public static class WmiSignedDriverFieldReader
{
    public sealed record Fields(
        string? Provider,
        string? Version,
        DateTimeOffset? Date,
        string? InfName,
        bool IsSigned);

    public static Fields Create(
        string? provider, string? version, string? rawDate,
        string? infName, bool isSigned) =>
        new(provider, version, ParseOptionalDate(rawDate), infName, isSigned);

    public static DateTimeOffset? ParseOptionalDate(string? rawDate)
    {
        // DMTF absolute date: 14 digits, '.', six fractional digits,
        // and signed 3-digit minute offset. Partial '*' components
        // cannot establish a verified driver date. Never decode arbitrary
        // WMI text or log the raw string (potential device metadata).
        if (rawDate is null || rawDate.Length != 25 ||
            rawDate[14] != '.' ||
            rawDate[21] is not ('+' or '-') ||
            !AllDigits(rawDate.AsSpan(0, 14)) ||
            !AllDigits(rawDate.AsSpan(15, 6)) ||
            !AllDigits(rawDate.AsSpan(22, 3)))
            return null;

        try
        {
            return new DateTimeOffset(
                ManagementDateTimeConverter.ToDateTime(rawDate));
        }
        catch (ArgumentException)
        {
            // Includes ArgumentOutOfRangeException from the WMI converter.
            return null;
        }
        catch (FormatException)
        {
            // DMTF input is untrusted optional metadata, not a scan gate.
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static bool AllDigits(ReadOnlySpan<char> text)
    {
        foreach (var value in text)
            if (value is < '0' or > '9')
                return false;
        return true;
    }
}
