using System.IO;
using Microsoft.Win32;

namespace TheEasyWayForDrivers.Desktop.Services;

public static class StartupRegistration
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName =
        "TheEasyWayForDrivers";

    public static bool IsEnabled()
    {
        using var key =
            Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: false);

        return key?.GetValue(ValueName) is string value &&
               !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(
        bool enabled)
    {
        using var key =
            Registry.CurrentUser.CreateSubKey(
                RunKeyPath,
                writable: true);

        if (!enabled)
        {
            key.DeleteValue(
                ValueName,
                throwOnMissingValue: false);

            return;
        }

        var executablePath =
            Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                "Impossibile determinare il percorso dell'applicazione.");
        }

        key.SetValue(
            ValueName,
            $"\"{executablePath}\"");
    }
}
