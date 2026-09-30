using System.IO;
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

    private readonly string _legacySettingsPath;

    public DesktopSettingsService()
    {
        var localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        SettingsPath =
            Path.Combine(
                localAppData,
                "OmegaDrive",
                "settings.json");

        _legacySettingsPath =
            Path.Combine(
                localAppData,
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

        TryDeleteLegacySettings();

        Current = preferences;
    }

    public void Reset()
    {
        Save(
            DesktopPreferences.Default with
            {
                FirstRunCompleted =
                    Current.FirstRunCompleted
            });
    }

    private DesktopPreferences Load()
    {
        var startupEnabled =
            StartupRegistration.IsEnabled();

        var sourcePath =
            File.Exists(SettingsPath)
                ? SettingsPath
                : File.Exists(_legacySettingsPath)
                    ? _legacySettingsPath
                    : null;

        if (sourcePath is null)
        {
            return DesktopPreferences.Default with
            {
                StartWithWindows = startupEnabled
            };
        }

        try
        {
            var json =
                File.ReadAllText(sourcePath);

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

    private void TryDeleteLegacySettings()
    {
        if (string.Equals(
                SettingsPath,
                _legacySettingsPath,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(_legacySettingsPath))
        {
            return;
        }

        try
        {
            File.Delete(_legacySettingsPath);

            var legacyDirectory =
                Path.GetDirectoryName(
                    _legacySettingsPath);

            if (!string.IsNullOrWhiteSpace(legacyDirectory) &&
                Directory.Exists(legacyDirectory) &&
                !Directory.EnumerateFileSystemEntries(
                    legacyDirectory).Any())
            {
                Directory.Delete(legacyDirectory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
