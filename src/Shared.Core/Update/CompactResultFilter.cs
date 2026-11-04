using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// The first post-scan screen must show all actually returned WUA updates,
/// even if a candidate is optional/advanced or a provider is not eligible
/// for one-click installation. Filters never create update candidates.
/// </summary>
public static class CompactResultFilter
{
    public static bool IsVisible(DriverUpdateInfo candidate, string? filter)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var eligible = QuickUpdatePolicy.IsEligible(candidate);
        return filter switch
        {
            "recommended" => eligible && !candidate.IsOptional,
            "optional" => eligible && candidate.IsOptional,
            "normal" => eligible,
            "advanced" => !eligible,
            // The default/home view is ALWAYS the full result set. In
            // particular Microsoft Update and advanced candidates aren't
            // silently hidden by a mismatch in quick-install eligibility.
            _ => true
        };
    }
}
