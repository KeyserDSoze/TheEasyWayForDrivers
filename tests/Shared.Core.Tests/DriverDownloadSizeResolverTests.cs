using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverDownloadSizeResolverTests
{
    [Fact]
    public void Resolve_PrefersPositiveMaximumSize()
    {
        Assert.Equal(
            4096,
            DriverDownloadSizeResolver.Resolve(
                4096,
                2048));
    }

    [Fact]
    public void Resolve_FallsBackToMinimumWhenMaximumIsZero()
    {
        Assert.Equal(
            2048,
            DriverDownloadSizeResolver.Resolve(
                0,
                2048));
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNeitherSizeIsUseful()
    {
        Assert.Null(
            DriverDownloadSizeResolver.Resolve(
                0,
                0));
    }
}
