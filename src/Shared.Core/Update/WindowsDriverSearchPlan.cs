using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Read-only WUA search plans. Microsoft Update is a separate catalog and
/// is queried only when its service is ALREADY registered with WUA.
/// The plan never registers services, changes policies, or installs updates.
/// </summary>
public static class WindowsDriverSearchPlan
{
    public const string MicrosoftUpdateServiceId =
        "7971f918-a847-4430-9279-4a52d1efe18d";

    public const string RecommendedCriteria =
        "IsInstalled=0 and IsHidden=0 and Type='Driver'";

    public const string BroadCriteria =
        "IsInstalled=0 and Type='Driver'";

    public const int WindowsUpdateServer = 2; // ssWindowsUpdate
    public const int OtherServer = 3; // ssOthers

    public sealed record Pass(
        string Name,
        string Criteria,
        int? ServerSelection,
        string? ServiceId,
        bool IncludePotentiallySuperseded,
        bool IsAdvancedCandidate);

    public static bool IsMicrosoftUpdateRegistered(IEnumerable<string?> serviceIds)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var target = Guid.Parse(MicrosoftUpdateServiceId);
        return serviceIds.Any(id => Guid.TryParse(id, out var parsed) &&
            parsed == target);
    }

    public static IReadOnlyList<Pass> Create(
        DriverSearchMode mode, bool microsoftUpdateRegistered)
    {
        var passes = new List<Pass>
        {
            new("Windows configurato", RecommendedCriteria,
                null, null, false, false)
        };

        if (mode != DriverSearchMode.Comprehensive)
            return passes;

        passes.Add(new Pass("Windows Update online", RecommendedCriteria,
            WindowsUpdateServer, null, false, false));
        passes.Add(new Pass("Windows configurato · avanzato", BroadCriteria,
            null, null, true, true));
        passes.Add(new Pass("Windows Update online · avanzato", BroadCriteria,
            WindowsUpdateServer, null, true, true));

        if (microsoftUpdateRegistered)
        {
            passes.Add(new Pass("Microsoft Update online", RecommendedCriteria,
                OtherServer, MicrosoftUpdateServiceId, false, false));
            passes.Add(new Pass("Microsoft Update online · avanzato", BroadCriteria,
                OtherServer, MicrosoftUpdateServiceId, true, true));
        }

        return passes;
    }

    public static DriverSearchPassStatus NotRegisteredStatus() =>
        new("Microsoft Update online", "NotRegistered", 0,
            "Microsoft Update non registrato in Windows; sorgente non interrogata. " +
            "Non significa zero aggiornamenti: OmegaDrive non modifica i servizi.");

    public static DriverSearchPassStatus RegistrationFailureStatus() =>
        new("Microsoft Update online", "Failed", 0,
            "Impossibile leggere i servizi WUA registrati; " +
            "disponibilità Microsoft Update non verificata.");
}
