namespace TheEasyWayForDrivers.Desktop.Settings;

public sealed record DesktopPreferences(
    bool StartWithWindows,
    bool MinimizeToTray,
    bool CloseToTray,
    bool ShowNotifications,
    bool CheckOnStartup = false,
    bool FirstRunCompleted = false)
{
    public static DesktopPreferences Default =>
        new(
            StartWithWindows: true,
            MinimizeToTray: true,
            CloseToTray: true,
            ShowNotifications: true,
            CheckOnStartup: false,
            FirstRunCompleted: false);
}
