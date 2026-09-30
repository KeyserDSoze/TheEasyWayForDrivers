using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ReleaseAssetDigestTests
{
    [Fact]
    public void TryParseSha256_AcceptsGitHubDigest()
    {
        var digest =
            "sha256:584da69e51b13ccb161e3ceb7f434d0d29ab571e8e04a889181e558ec31bfa86";

        var result = ReleaseAssetDigest.TryParseSha256(digest, out var hash);

        Assert.True(result);
        Assert.Equal(
            "584da69e51b13ccb161e3ceb7f434d0d29ab571e8e04a889181e558ec31bfa86",
            hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:1234")]
    [InlineData("md5:584da69e51b13ccb161e3ceb7f434d0d29ab571e8e04a889181e558ec31bfa86")]
    [InlineData("sha256:584da69e51b13ccb161e3ceb7f434d0d29ab571e8e04a889181e558ec31bfa8Z")]
    public void TryParseSha256_RejectsInvalidDigest(string? digest)
    {
        Assert.False(ReleaseAssetDigest.TryParseSha256(digest, out _));
    }
}
