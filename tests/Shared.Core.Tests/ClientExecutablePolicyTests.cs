using TheEasyWayForDrivers.Core.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class ClientExecutablePolicyTests
{
    [Fact]
    public void IsAuthorizedDesktopPath_AcceptsInstalledDesktop()
    {
        Assert.True(ClientExecutablePolicy.IsAuthorizedDesktopPath(
            @"C:\Program Files\TheEasyWayForDrivers\Desktop\App2.Desktop.exe",
            @"C:\Program Files"));
    }

    [Fact]
    public void IsAuthorizedDesktopPath_IsCaseInsensitive()
    {
        Assert.True(ClientExecutablePolicy.IsAuthorizedDesktopPath(
            @"c:\program files\theeasywayfordrivers\desktop\APP2.DESKTOP.EXE",
            @"C:\Program Files"));
    }

    [Theory]
    [InlineData(@"C:\Temp\App2.Desktop.exe")]
    [InlineData(@"C:\Program Files\TheEasyWayForDrivers\Desktop\Other.exe")]
    [InlineData(null)]
    public void IsAuthorizedDesktopPath_RejectsUnexpectedClient(string? path)
    {
        Assert.False(ClientExecutablePolicy.IsAuthorizedDesktopPath(
            path,
            @"C:\Program Files"));
    }
}
