using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Second trust boundary for a privileged WUA installation. The UI policy
/// is advisory; the service rechecks each GUID using a non-hidden,
/// non-superseded Type=Driver WUA search before AcceptEula/download.
/// </summary>
public static class WuaInstallRecheckPolicy
{
    public const string Criteria = WindowsDriverSearchPlan.RecommendedCriteria;
    public const bool IncludePotentiallySuperseded = false;

    public static bool CanStage(
        string? requestedId, string? resultId,
        bool? hidden, bool? browseOnly,
        string? title, string? driverClass)
    {
        return Guid.TryParse(requestedId, out var requestedGuid) &&
               Guid.TryParse(resultId, out var resultGuid) &&
               requestedGuid == resultGuid &&
               hidden == false && browseOnly == false &&
               WuaDriverCandidateReader.IsValidSafetyText(
                   title, WuaDriverCandidateReader.MaximumTitleLength) &&
               WuaDriverCandidateReader.IsValidSafetyText(
                   driverClass, WuaDriverCandidateReader.MaximumClassLength) &&
               !QuickUpdatePolicy.IsFirmwareOrBios(title, driverClass);
    }
}
