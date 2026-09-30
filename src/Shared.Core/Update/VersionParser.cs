namespace TheEasyWayForDrivers.Core.Update;

public static class VersionParser
{
    public static Version ParseTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var prereleaseSeparator = normalized.IndexOf('-');
        if (prereleaseSeparator >= 0)
        {
            normalized = normalized[..prereleaseSeparator];
        }

        if (!Version.TryParse(normalized, out var version))
        {
            throw new FormatException($"Invalid release tag '{tag}'.");
        }

        return version;
    }
}
