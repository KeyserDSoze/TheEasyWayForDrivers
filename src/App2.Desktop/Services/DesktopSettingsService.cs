using System.Text.Json;
using TheEasyWayForDrivers.Desktop.Settings;

namespace TheEasyWayForDrivers.Desktop.Services;

public sealed class DesktopSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    public DesktopSettingsService()
    {
        SettingsPath =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "TheEasyWayForDrivers",
                "settings.json");

        Current = Load();
    }

    public string SettingsPath { get; }

    public DesktopPreferences Current { get; private set; }

    public void Save(
        DesktopPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        StartupRegistration.SetEnabled(
            preferences.StartWithWindows);

        var directory =
            Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException(
                "Percorso impostazioni non valido.");

        Directory.CreateDirectory(directory);

        var temporaryPath =
            SettingsPath + ".tmp";

        var json =
            JsonSerializer.Serialize(
                preferences,
                JsonOptions);

        File.WriteAllText(
            temporaryPath,
            json);

        File.Move(
            temporaryPath,
            SettingsPath,
            overwrite: true);

        Current = preferences;
    }

    public void Reset()
    {
        Save(DesktopPreferences.Default);
    }

    private DesktopPreferences Load()
    {
        var startupEnabled =
            StartupRegistration.IsEnabled();

        if (!File.Exists(SettingsPath))
        {
            return DesktopPreferences.Default with
            {
                StartWithWindows = startupEnabled
            };
        }

        try
        {
            var json =
                File.ReadAllText(SettingsPath);

            var loaded =
                JsonSerializer.Deserialize<DesktopPreferences>(
                    json,
                    JsonOptions);

            return (loaded ?? DesktopPreferences.Default) with
            {
                StartWithWindows = startupEnabled
            };
        }
        catch (JsonException)
        {
            return DesktopPreferences.Default with
            {
                StartWithWindows = startupEnabled
            };
        }
        catch (IOException)
        {
            return DesktopPreferences.Default with
            {
                StartWithWindows = startupEnabled
            };
        }
        catch (UnauthorizedAccessException)
        {
            return DesktopPreferences.Default with
            {
                StartWithWindows = startupEnabled
            };
        }
    }
}
