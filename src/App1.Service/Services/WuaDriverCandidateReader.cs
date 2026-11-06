using System.Globalization;
using TheEasyWayForDrivers.Core.Models;
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
    public static string? ReadPrimaryHardwareId(object update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return OptionalString(() => ((dynamic)update).DriverHardwareID);
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

        var title = OptionalString(() => ((dynamic)update).Title);
        var driverClass = OptionalString(() => ((dynamic)update).DriverClass);
        var isHidden = OptionalBool(() => ((dynamic)update).IsHidden);
        var browseOnly = OptionalBool(() => ((dynamic)update).BrowseOnly);

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
            OptionalString(() => ((dynamic)update).Description),
            driverClass,
            OptionalString(() => ((dynamic)update).DriverProvider),
            null,
            DriverDownloadSizeResolver.Resolve(
                OptionalLong(() => ((dynamic)update).MaxDownloadSize),
                OptionalLong(() => ((dynamic)update).MinDownloadSize)),
            OptionalBool(() => ((dynamic)update).IsDownloaded) ?? false,
            OptionalString(() => ((dynamic)update).DriverManufacturer),
            OptionalString(() => ((dynamic)update).DriverModel),
            primaryHardwareId,
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

    private static string? OptionalString(Func<object?> getter)
    {
        var value = Optional(getter);
        return value as string;
    }

    private static bool? OptionalBool(Func<object?> getter)
    {
        var value = Optional(getter);
        if (value is null)
            return null;
        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not OutOfMemoryException)
        {
            return null;
        }
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
