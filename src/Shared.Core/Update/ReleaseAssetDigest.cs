namespace TheEasyWayForDrivers.Core.Update;

public static class ReleaseAssetDigest
{
    private const string Sha256Prefix = "sha256:";

    public static bool TryParseSha256(string? value, out string hash)
    {
        hash = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = value[Sha256Prefix.Length..].Trim();
        if (candidate.Length != 64)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        hash = candidate.ToLowerInvariant();
        return true;
    }
}
