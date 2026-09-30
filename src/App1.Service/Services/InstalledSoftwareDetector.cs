using System.Security;
using Microsoft.Win32;

namespace TheEasyWayForDrivers.ServiceApp.Services;

internal static class InstalledSoftwareDetector
{
    public static InstalledSoftwareInfo Find(
        Func<string, bool> displayNamePredicate)
    {
        ArgumentNullException.ThrowIfNull(displayNamePredicate);

        foreach (var view in new[]
                 {
                     RegistryView.Registry64,
                     RegistryView.Registry32
                 })
        {
            var result = Find(view, displayNamePredicate);
            if (result.IsInstalled)
            {
                return result;
            }
        }

        return new InstalledSoftwareInfo(false, null, null);
    }

    private static InstalledSoftwareInfo Find(
        RegistryView view,
        Func<string, bool> displayNamePredicate)
    {
        try
        {
            using var localMachine =
                RegistryKey.OpenBaseKey(
                    RegistryHive.LocalMachine,
                    view);

            using var uninstall =
                localMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (uninstall is null)
            {
                return new InstalledSoftwareInfo(false, null, null);
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey =
                    uninstall.OpenSubKey(subKeyName);

                var displayName =
                    subKey?.GetValue("DisplayName") as string;

                if (string.IsNullOrWhiteSpace(displayName) ||
                    !displayNamePredicate(displayName))
                {
                    continue;
                }

                var version =
                    subKey?.GetValue("DisplayVersion") as string;

                return new InstalledSoftwareInfo(
                    true,
                    displayName.Trim(),
                    string.IsNullOrWhiteSpace(version)
                        ? null
                        : version.Trim());
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SecurityException)
        {
        }

        return new InstalledSoftwareInfo(false, null, null);
    }
}

internal sealed record InstalledSoftwareInfo(
    bool IsInstalled,
    string? DisplayName,
    string? Version);
