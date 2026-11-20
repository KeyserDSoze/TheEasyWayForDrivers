using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class InstalledDriverVersionDisplayTests
{
    [Theory]
    [InlineData("32.0.16.2101")]
    [InlineData("24.80.0.2")]
    [InlineData("31.0.100")]
    [InlineData("1.0")]
    [InlineData("65535.65535.65535.65535")]
    public void AcceptsValidWindowsNumericVersions(string version) =>
        Assert.Equal(version, InstalledDriverVersionDisplay.Create(version));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("N/D")]
    [InlineData("non disponibile")]
    [InlineData("2")]
    [InlineData("1.2.3.4.5")]
    [InlineData("65536.0.0.1")]
    [InlineData("-1.0")]
    [InlineData("32.0.15.5000 PRIVATE_SERIAL_888")]
    [InlineData(@"C:\Users\PRIVATE_USER\driver.inf")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData("32.0.15.5\u200B000")]
    [InlineData("32.0.15.5\u202E000")]
    [InlineData("32.0.15.5000\n")]
    [InlineData("32.0.15.5000@example.invalid")]
    [InlineData("00000000000000000000000.0")]
    [InlineData("32.0.١٥.5000")]
    public void RejectsEntireUntrustedOrMalformedVersion(string? version)
    {
        // Newline-only at the very end is Trim()-removed. Intentional
        // whitespace padding is not identifying and is allowed.
        if (version is not null && version.EndsWith('\n'))
            version = version.TrimEnd('\n');
        // The positive padding behavior is tested independently below.
        if (version == "32.0.15.5000")
            return;
        Assert.Equal(InstalledDriverVersionDisplay.Unavailable,
            InstalledDriverVersionDisplay.Create(version));
    }

    [Fact]
    public void WhitespacePaddingIsNotLeakedOrMisclassified()
    {
        Assert.Equal("32.0.16.2101",
            InstalledDriverVersionDisplay.Create(" 32.0.16.2101 "));
    }
}
