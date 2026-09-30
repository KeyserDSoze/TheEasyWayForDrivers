using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class VersionParserTests
{
    [Theory]
    [InlineData("v0.0.1", 0, 0, 1)]
    [InlineData("0.0.42", 0, 0, 42)]
    [InlineData("v1.2.3-preview.1", 1, 2, 3)]
    public void ParseTag_ReturnsExpectedVersion(
        string tag,
        int major,
        int minor,
        int build)
    {
        var version = VersionParser.ParseTag(tag);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
    }

    [Fact]
    public void ParseTag_InvalidTag_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => VersionParser.ParseTag("not-a-version"));
    }
}
