using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Extra, REVIEW-ONLY WUA correlation for devices whose Win32_PnPEntity
/// hardware IDs contain a SUBSYS/revision but CompatibleID is unavailable.
/// An official WUA entry may declare only the public vendor/model token.
/// THIS IS NOT a Windows-selected driver, verified OEM compatibility, or
/// authority to enable one-click installation.
/// </summary>
public static class WuaGenericModelCorrelation
{
    public const int MaximumReviewHintsPerDevice = 16;

    private static readonly HashSet<string> OfficialSources =
        WindowsDriverSearchPlan.Create(DriverSearchMode.Comprehensive, true)
            .Select(pass => pass.Name)
            .Append("Windows Update")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsWeakModelOnlyMatch(
        DriverInfo device, DriverUpdateInfo update)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(update);

        if (!Guid.TryParse(update.Id, out _) ||
            !OfficialSources.Contains(update.SearchSource) ||
            QuickUpdatePolicy.IsFirmwareOrBios(update.Title, update.DriverClass) ||
            DriverUpdateMatcher.MatchStrength(device, update) != 0)
            return false;

        // Only strong Windows device hardware IDs may provide a model key.
        // Do not use a device name, manufacturer string, Device Instance ID,
        // compatible-ID-only fallback or unrecognized bus. A WUA GUID with
        // a different OEM SUBSYS must not match the generic model either.
        var models = device.HardwareIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => new
            {
                Raw = id!,
                Key = MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id)
            })
            .Where(model => model.Key is not null &&
                !model.Raw.Equals(model.Key, StringComparison.OrdinalIgnoreCase) &&
                IsSpecificModelBus(model.Key))
            .Select(model => model.Key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (models.Count == 0)
            return false;

        var declared = new[] { update.HardwareId }
            .Concat((update.AdditionalHardwareIds ?? [])
                .Take(WuaDriverEntryLimits.MaximumAdditionalIds));

        return declared.Any(id =>
            id is not null &&
            models.Contains(id) &&
            // WUA ID must itself be exactly the generic model, without
            // SUBSYS or any unvalidated suffix. Never trim untrusted IDs.
            string.Equals(
                MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id),
                id.ToUpperInvariant(), StringComparison.Ordinal));
    }

    private static bool IsSpecificModelBus(string key) =>
        key.StartsWith(@"PCI\", StringComparison.Ordinal) ||
        key.StartsWith(@"USB\", StringComparison.Ordinal) ||
        key.StartsWith(@"HDAUDIO\", StringComparison.Ordinal) ||
        key.StartsWith(@"INTELAUDIO\", StringComparison.Ordinal) ||
        key.StartsWith(@"SWC\", StringComparison.Ordinal);
}
