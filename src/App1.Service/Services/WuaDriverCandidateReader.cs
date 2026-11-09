using System.Globalization;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Reads ONLY optional per-update WUA COM properties. The getter itself must
/// run inside the exception boundary: SafeString(update.Title) is NOT safe,
/// because update.Title is evaluated before SafeString is entered.
/// A failed optional getter must not erase an otherwise complete WUA pass.
/// Structural WUA enumeration, Identity.UpdateID and search-result status
/// remain mandatory and are deliberately read outside this helper.
/// </summary>
public static class WuaDriverCandidateReader
{
    // Malformed/unbounded COM strings must not flow into the Desktop,
    // exported diagnostics or firmware/BIOS safety checks. A genuinely
    // valid WUA update GUID is preserved as review evidence.
    public const int MaximumTitleLength = 512;
    public const int MaximumClassLength = 128;
    public const int MaximumDescriptiveTextLength = 4096;
    public const int MaximumVendorTextLength = 1024;

    /// <summary>COM safety fields shared by discovery and privileged install
    /// recheck. A null boolean is unreadable, never a safe false.</summary>
    public sealed record SafetyMetadata(
        string? Title, string? DriverClass, bool? IsHidden, bool? BrowseOnly);

    public static SafetyMetadata ReadSafetyMetadata(object update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return new(
            OptionalString(() => ((dynamic)update).Title, MaximumTitleLength),
            OptionalString(() => ((dynamic)update).DriverClass, MaximumClassLength),
            OptionalBool(() => ((dynamic)update).IsHidden),
            OptionalBool(() => ((dynamic)update).BrowseOnly));
    }

    public static bool IsValidSafetyText(string? text, int maximumLength) =>
        !string.IsNullOrWhiteSpace(text) &&
        text.Length <= maximumLength &&
        !text.Any(c => char.IsControl(c) || char.IsSurrogate(c) ||
            char.GetUnicodeCategory(c) is
                UnicodeCategory.Format or UnicodeCategory.LineSeparator or
                UnicodeCategory.ParagraphSeparator);

    public static string? ReadPrimaryHardwareId(object update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var id = OptionalString(() => ((dynamic)update).DriverHardwareID,
            WuaDriverEntryLimits.MaximumIdLength);
        return PnpIdentifierFallback.IsStrictSingleId(id) ? id : null;
    }

    public static DriverUpdateInfo Read(
        object update, string updateId, string? primaryHardwareId,
        IReadOnlyList<string> additionalIds, bool additionalIdsIncomplete,
        bool advancedCandidate, string searchSource)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(additionalIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchSource);

        var safety = ReadSafetyMetadata(update);
        var title = safety.Title;
        var driverClass = safety.DriverClass;
        // The overload is public for tests and the WUA service; never
        // trust a raw supplied primary ID more than the COM getter path.
        var safePrimaryHardwareId =
            PnpIdentifierFallback.IsStrictSingleId(primaryHardwareId) &&
            primaryHardwareId!.Length <= WuaDriverEntryLimits.MaximumIdLength
                ? primaryHardwareId : null;
        var isHidden = safety.IsHidden;
        var browseOnly = safety.BrowseOnly;

        // These fields affect the firmware/BIOS gate or whether an offer
        // is ordinary. If any cannot be read, preserve the GUID as review
        // evidence only. Do not turn failed COM getters into 'safe=false'.
        var safetyUnknown = string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(driverClass) ||
            isHidden is null || browseOnly is null;

        return new DriverUpdateInfo(
            updateId,
            string.IsNullOrWhiteSpace(title)
                ? "Driver Windows Update (metadati non verificati)"
                : title,
            OptionalString(() => ((dynamic)update).Description,
                MaximumDescriptiveTextLength),
            driverClass,
            OptionalString(() => ((dynamic)update).DriverProvider,
                MaximumVendorTextLength),
            null,
            DriverDownloadSizeResolver.Resolve(
                OptionalLong(() => ((dynamic)update).MaxDownloadSize),
                OptionalLong(() => ((dynamic)update).MinDownloadSize)),
            OptionalBool(() => ((dynamic)update).IsDownloaded) ?? false,
            OptionalString(() => ((dynamic)update).DriverManufacturer,
                MaximumVendorTextLength),
            OptionalString(() => ((dynamic)update).DriverModel,
                MaximumVendorTextLength),
            safePrimaryHardwareId,
            OptionalDate(() => ((dynamic)update).DriverVerDate),
            isHidden ?? true,
            browseOnly ?? true,
            advancedCandidate || safetyUnknown,
            searchSource,
            additionalIds,
            additionalIdsIncomplete);
    }

    private static object? Optional(Func<object?> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            // An inaccessible optional COM field is unknown, not an
            // empty WUA pass, a valid default or installation permission.
            return null;
        }
    }

    private static string? OptionalString(
        Func<object?> getter, int maximumLength)
    {
        var value = Optional(getter);
        // Do not ToString() a hostile COM variant. WUA titles and classes
        // are safety-sensitive: a malformed value becomes UNKNOWN, forcing
        // an otherwise confirmed GUID into advanced/review-only status.
        return value is string text &&
            IsValidSafetyText(text, maximumLength) ? text : null;
    }

    private static bool? OptionalBool(Func<object?> getter)
    {
        // VARIANT_BOOL is a genuine bool; "False" or 0 from a malformed
        // COM property is not evidence that an update is safe to install.
        return Optional(getter) is bool flag ? flag : null;
    }

    private static long? OptionalLong(Func<object?> getter)
    {
        var value = Optional(getter);
        if (value is null)
            return null;
        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static DateTimeOffset? OptionalDate(Func<object?> getter)
    {
        var value = Optional(getter);
        if (value is null)
            return null;
        try
        {
            return value is DateTime date
                ? new DateTimeOffset(date)
                : new DateTimeOffset(Convert.ToDateTime(
                    value, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
