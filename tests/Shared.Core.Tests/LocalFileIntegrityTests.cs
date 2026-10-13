using System.Security.Cryptography;
using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LocalFileIntegrityTests
{
    [Fact]
    public void KnownSha256CanBeVerifiedAgainstExpectedDigest()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "omega", Encoding.ASCII);
            var expected = Convert.ToHexString(
                SHA256.HashData(Encoding.ASCII.GetBytes("omega")));
            var result = LocalFileIntegrity.Check(file, 1024, expected.ToLowerInvariant());
            Assert.Equal(LocalFileIntegrity.DigestStatus.MatchesExpected, result.Status);
            Assert.Equal(expected, result.Sha256);
            Assert.Equal(5, result.SizeBytes);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void WrongDigestIsNotClaimedAsVerified()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "omega", Encoding.ASCII);
            Assert.Equal(LocalFileIntegrity.DigestStatus.DoesNotMatch,
                LocalFileIntegrity.Check(file, 1024, new string('0', 64)).Status);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData(null, LocalFileIntegrity.DigestStatus.NoExpectedDigest)]
    [InlineData("", LocalFileIntegrity.DigestStatus.NoExpectedDigest)]
    [InlineData("abc123", LocalFileIntegrity.DigestStatus.InvalidExpectedDigest)]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg",
        LocalFileIntegrity.DigestStatus.InvalidExpectedDigest)]
    public void DigestWithoutTrustedReferenceIsNotClaimedValid(
        string? expected, LocalFileIntegrity.DigestStatus outcome)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "test");
            Assert.Equal(outcome, LocalFileIntegrity.Check(file, 1024, expected).Status);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void OversizedFileIsRejected()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[50]);
            Assert.Throws<InvalidDataException>(() =>
                LocalFileIntegrity.Check(file, 32));
        }
        finally { File.Delete(file); }
    }
}
