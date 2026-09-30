namespace TheEasyWayForDrivers.Desktop.Settings;

public sealed record DesktopPreferences(
    bool StartWithWindows,
    bool MinimizeToTray,
    bool CloseToTray,
    bool ShowNotifications)
{
    public static DesktopPreferences Default =>
        new(
            StartWithWindows: true,
            MinimizeToTray: true,
            CloseToTray: true,
            ShowNotifications: true);
}
