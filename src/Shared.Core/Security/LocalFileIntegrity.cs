using System.Security.Cryptography;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// SHA-256 is evidence of file identity only. It proves authenticity only
/// when compared with an expected digest received via a trusted channel.
/// </summary>
public static class LocalFileIntegrity
{
    public enum DigestStatus
    {
        NoExpectedDigest,
        MatchesExpected,
        DoesNotMatch,
        InvalidExpectedDigest
    }

    public sealed record Result(string Sha256, DigestStatus Status, long SizeBytes);

    public static Result Check(string path, long maxBytes, string? expectedSha256 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new InvalidDataException("File oltre il limite previsto per la verifica di integrità.");

        // Streaming digest, bounded by the size check. No package code runs.
        var digest = SHA256.HashData(stream);
        var hex = Convert.ToHexString(digest);
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return new Result(hex, DigestStatus.NoExpectedDigest, stream.Length);

        var expected = expectedSha256.Trim();
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            return new Result(hex, DigestStatus.InvalidExpectedDigest, stream.Length);

        var expectedBytes = Convert.FromHexString(expected);
        return new Result(hex,
            CryptographicOperations.FixedTimeEquals(digest, expectedBytes)
                ? DigestStatus.MatchesExpected
                : DigestStatus.DoesNotMatch,
            stream.Length);
    }
}
