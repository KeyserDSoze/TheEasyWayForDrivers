using System.Diagnostics;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Validate the already-extracted OmegaDrive binaries before an elevated
/// installer stops its running service or replaces installed directories.
/// The ZIP structure gate is independent; this checks the actual PE versions.
/// </summary>
public static class SetupPayloadPreflight
{
    public static void Validate(string stagingDirectory, string expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);

        if (!Version.TryParse(expectedVersion, out var version) ||
            version.Build < 0 ||
            !string.Equals(expectedVersion,
                $"{version.Major}.{version.Minor}.{version.Build}",
                StringComparison.Ordinal))
            throw new InvalidDataException(
                "Versione del setup non valida per il payload.");

        ValidateComponent(Path.Combine(stagingDirectory, "Service",
            "App1.Service.exe"), expectedVersion, "service");
        ValidateComponent(Path.Combine(stagingDirectory, "Desktop",
            "App2.Desktop.exe"), expectedVersion, "desktop");
    }

    private static void ValidateComponent(string path, string expected,
        string component)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"Eseguibile {component} mancante, vuoto o collegato.");

        // This is the SAME Major.Minor.Build comparison already enforced
        // by SetupEngine.ValidateInstalledPayload *after* installation,
        // applied before any Windows service or Program Files mutation.
        var rawVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
        if (!Version.TryParse(rawVersion, out var actual))
            throw new InvalidDataException(
                $"Versione di {component} non leggibile.");

        var normalized =
            $"{actual.Major}.{actual.Minor}.{Math.Max(actual.Build, 0)}";
        if (!string.Equals(normalized, expected,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Versione di {component} diversa da quella del setup.");
    }
}
