using TheEasyWayForDrivers.Core.Progress;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ProgressMapperTests
{
    [Theory]
    [InlineData(0, 15)]
    [InlineData(25, 25)]
    [InlineData(50, 35)]
    [InlineData(75, 45)]
    [InlineData(100, 55)]
    public void Map_MapsDownloadRange(int sourcePercent, int expected)
    {
        Assert.Equal(expected, ProgressMapper.Map(sourcePercent, 15, 55));
    }

    [Theory]
    [InlineData(-10, 15)]
    [InlineData(120, 55)]
    public void Map_ClampsInput(int sourcePercent, int expected)
    {
        Assert.Equal(expected, ProgressMapper.Map(sourcePercent, 15, 55));
    }
}
