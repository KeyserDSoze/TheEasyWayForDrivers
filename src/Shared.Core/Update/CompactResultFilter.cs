using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// The first post-scan screen must show all actually returned WUA updates,
/// even if a candidate is optional/advanced or a provider is not eligible
/// for one-click installation. Filters never create update candidates.
/// </summary>
public static class CompactResultFilter
{
    /// <summary>
    /// A WUA BrowseOnly offer remains visible for informed review, but
    /// must not count as recommended or quick-installable. Hidden,
    /// advanced-pass or unknown-origin optional flags belong in the
    /// advanced/not-verified category, never in a trusted WUA bucket.
    /// </summary>
    public static bool IsOptionalReview(DriverUpdateInfo candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.IsOptional &&
               !candidate.IsHidden &&
               !candidate.IsAdvancedCandidate &&
               QuickUpdatePolicy.IsKnownWindowsUpdateSource(
                   candidate.SearchSource);
    }

    public static bool IsVisible(DriverUpdateInfo candidate, string? filter)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var eligible = QuickUpdatePolicy.IsEligible(candidate);
        var optionalReview = IsOptionalReview(candidate);
        return filter switch
        {
            "recommended" => eligible,
            "optional" => optionalReview,
            "normal" => eligible,
            // These three visible categories are DISJOINT and mirror
            // the Home counters; the default still shows every result.
            "advanced" => !eligible && !optionalReview,
            _ => true
        };
    }
}
