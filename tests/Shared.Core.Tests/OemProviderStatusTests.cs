using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemProviderStatusTests
{
    [Fact]
    public void CompanionStatus_ShowsInstalledVersion()
    {
        var status = CreateStatus(
            installed: true,
            version: "25.4.36.6");

        Assert.Equal(
            "Installato · v25.4.36.6",
            status.CompanionStatus);
    }

    [Fact]
    public void CompanionStatus_ShowsNotInstalled()
    {
        var status = CreateStatus(
            installed: false,
            version: null);

        Assert.Equal(
            "Non installato",
            status.CompanionStatus);
    }

    [Fact]
    public void CompanionStatus_ShowsNotVerified()
    {
        var status = CreateStatus(
            installed: null,
            version: null);

        Assert.Equal(
            "Non verificato",
            status.CompanionStatus);
    }

    private static OemProviderStatus CreateStatus(
        bool? installed,
        string? version) =>
        new(
            "intel",
            "Intel",
            2,
            installed,
            version,
            "Companion ufficiale",
            "https://www.intel.com/content/www/us/en/support/detect.html",
            "Test");
}
