using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #477: the optional HP CMSL reader cannot be launched for another OEM
/// based on an arbitrary substring in the selected IPC provider record.
/// </summary>
public sealed class HpAdvancedCatalogQueryPolicyTests
{
    private static OemProviderStatus Provider(
        string? manufacturer, int count = 1, string id = "system-oem") =>
        new(id, "Selected OEM", count, null, null,
            "Driver metadata only", "https://support.hp.com/",
            "IPC status", manufacturer, "EliteBook 840");

    [Theory]
    [InlineData("HP")]
    [InlineData("HP Inc.")]
    [InlineData("Hewlett-Packard")]
    [InlineData("Hewlett-Packard Company")]
    [InlineData("Hewlett Packard")]
    public void RealHpPcCanRequestReadOnlyAdvancedCatalog(
        string manufacturer)
    {
        Assert.True(HpAdvancedCatalogQueryPolicy.CanQuery(
            Provider(manufacturer)));
    }

    [Theory]
    [InlineData("NotHP")]
    [InlineData("Other HP")]
    [InlineData("HPFake")]
    [InlineData("Hewlett Packard Enterprise")]
    [InlineData("Other Hewlett-Packard")]
    [InlineData("HP\n Inc.")]
    [InlineData("HP\tInc.")]
    [InlineData("HP\u202E Inc.")]
    [InlineData("HP\u00A0Inc.")]
    [InlineData("Dell Inc.")]
    [InlineData("Lenovo")]
    [InlineData("")]
    public void MalformedOrOtherPcCannotLaunchHpModule(
        string manufacturer)
    {
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(
            Provider(manufacturer)));
    }

    [Fact]
    public void MissingUnrecognizedAndInapplicableIpcProviderCannotRunCmsl()
    {
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(null));
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(Provider(null)));
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(
            Provider("HP", count: 0)));
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(
            Provider("HP", count: -1)));
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(
            Provider("HP", id: "intel")));
        Assert.False(HpAdvancedCatalogQueryPolicy.CanQuery(
            Provider("HP", id: "SYSTEM-OEM")));
    }

    [Fact]
    public void FailureMessageHasNoDynamicWmiOrPowerShellFields()
    {
        var text = HpAdvancedCatalogQueryPolicy.UnavailableMessage;
        Assert.Contains("non completata", text);
        Assert.Contains("nessun driver installato", text);
        Assert.DoesNotContain("PRIVATE_", text);
        Assert.DoesNotContain("C:\\", text);
        Assert.DoesNotContain("@", text);
        Assert.DoesNotContain("serial", text,
            StringComparison.OrdinalIgnoreCase);
    }
}
