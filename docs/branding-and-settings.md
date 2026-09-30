# Branding and desktop settings

## Visual identity

The desktop and setup share the same Windows icon: a blue shield containing a
simplified road/chevron mark. The design is intentionally readable at small
notification-area sizes and communicates both system protection and the
"easy way" driver theme.

The icon is embedded as the application icon for:

- App2.Desktop.exe;
- the WPF main window;
- the notification-area icon;
- App3.Setup.exe and therefore the Windows uninstall entry.

The tray icon is extracted from the running executable instead of maintaining a
separate icon file at runtime.

## Per-user settings

Desktop preferences are stored in:

`%LocalAppData%\TheEasyWayForDrivers\settings.json`

The current settings are:

- start TheEasyWayForDrivers with Windows;
- minimize to the notification area;
- close to the notification area instead of exiting;
- show notification-area messages.

Changing "start with Windows" updates the current user's
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry immediately.

Updates preserve that registry preference rather than forcing startup back on.

## About diagnostics

The About page reports the application version, .NET runtime, process
architecture, executable path and whether the current PE contains a populated
Authenticode Security Directory.

That check reports the presence of an embedded signature. Windows remains the
authority for trust-chain and publisher validation.
