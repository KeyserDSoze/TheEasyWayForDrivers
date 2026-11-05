using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaDriverVersionHintTests
{
    [Theory]
    [InlineData("Microsoft Corporation AudioProcessingObject Driver Update (1.0.4.55794)", "1.0.4.55794")]
    [InlineData("NVIDIA - Display - 32.0.15.6614", "32.0.15.6614")]
    [InlineData("Intel - net - 23.130.1.1", "23.130.1.1")]
    [InlineData("Intel Corporation - System - 10.1.16.8 ", "10.1.16.8")]
    [InlineData("Driver (1.2.3)", "1.2.3")]
    [InlineData("Driver (1.2.3.4.5)", "1.2.3.4.5")]
    public void KnownWindowsDriverTitleHasExplicitDisplayOnlyHint(
        string title, string expected) =>
        Assert.Equal(expected, WuaDriverVersionHint.FromTitle(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NVIDIA - Display")]
    [InlineData("Microsoft - Display - 32.0.15.6614 beta")]
    [InlineData("AudioProcessingObject Driver Update (KB1234567)")]
    [InlineData("Audio Processing Driver Update (1.2)")]
    [InlineData("Driver - 2026.11.05")]
    [InlineData("Driver (2024.01.20)")]
    [InlineData("Driver (1.2.3.4.5.6)")]
    [InlineData("Driver (1.2.3) trailing text")]
    [InlineData("nvidia.com/download/1.2.3")]
    [InlineData("System Rev (1.x.3.4)")]
    public void UncertainTitlesNeverInventComparableVersions(string? title) =>
        Assert.Null(WuaDriverVersionHint.FromTitle(title));

    [Fact]
    public void ExcessivelyLongUntrustedTitlesDoNotGetParsed() =>
        Assert.Null(WuaDriverVersionHint.FromTitle(
            new string('X', 351) + " (1.2.3.4)"));
}
