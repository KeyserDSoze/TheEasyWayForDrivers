using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Advanced read-only OEM catalog button security/lifecycle regression #478.
/// </summary>
public sealed class AdvancedOemCatalogQueryPolicyTests
{
    private static OemProviderStatus Provider(
        string? manufacturer, string? model = "Latitude 7440",
        int detectedCount = 1, string id = "system-oem") =>
        new(id, "Source from IPC", detectedCount, null, null,
            "Catalog metadata", "https://example.invalid",
            "Private IPC status", manufacturer, model);

    [Theory]
    [InlineData("Dell Inc.", "Latitude 7440")]
    [InlineData("DELL Technologies", "XPS 15 9530")]
    [InlineData("Dell", "Precision 5690")]
    public void DellRealApplicableModelsAreAllowed(
        string manufacturer, string model)
    {
        Assert.True(AdvancedOemCatalogQueryPolicy.TryGetDellModel(
            Provider(manufacturer, model), out var safeModel));
        Assert.Equal(model, safeModel);
    }

    [Theory]
    [InlineData("NotDell", "Latitude 7440")]
    [InlineData("Other Dell", "Latitude 7440")]
    [InlineData("HP", "Latitude 7440")]
    [InlineData("Dell Inc.", "Unknown")]
    [InlineData("Dell Inc.", "To be filled by O.E.M.")]
    [InlineData("Dell Inc.", @"C:\Users\PRIVATE_USER\Latitude")]
    [InlineData("Dell Inc.", @"PCI\VEN_1234&DEV_5678")]
    [InlineData("Dell Inc.", "owner@example.invalid")]
    [InlineData("Dell Inc.", "Latitude 7440\n")]
    [InlineData("Dell Inc.", " Latitude 7440 ")]
    [InlineData("Dell\tInc.", "Latitude 7440")]
    [InlineData("Dell Inc.", "Latitude\u202E 7440")]
    [InlineData("Dell Inc.", "Latitude\u00A0 7440")]
    public void DellUntrustedIpcCannotTriggerCatalog(
        string manufacturer, string model)
    {
        Assert.False(AdvancedOemCatalogQueryPolicy.TryGetDellModel(
            Provider(manufacturer, model), out var safeModel));
        Assert.Equal(string.Empty, safeModel);
    }

    [Fact]
    public void DellInapplicableAndOtherProviderRecordsAreRejected()
    {
        Assert.False(AdvancedOemCatalogQueryPolicy.TryGetDellModel(null, out _));
        Assert.False(AdvancedOemCatalogQueryPolicy.TryGetDellModel(
            Provider("Dell", detectedCount: 0), out _));
        Assert.False(AdvancedOemCatalogQueryPolicy.TryGetDellModel(
            Provider("Dell", detectedCount: -1), out _));
        Assert.False(AdvancedOemCatalogQueryPolicy.TryGetDellModel(
            Provider("Dell", id: "nvidia"), out _));
        Assert.False(AdvancedOemCatalogQueryPolicy.TryGetDellModel(
            Provider("Dell", model: null), out _));
    }

    [Theory]
    [InlineData("Lenovo")]
    [InlineData("Lenovo Group Limited")]
    [InlineData(" LENOVO ")]
    public void OnlyApplicableLenovoCanQueryModule(string manufacturer)
    {
        Assert.True(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(
            Provider(manufacturer)));
    }

    [Theory]
    [InlineData("NotLenovo")]
    [InlineData("Other Lenovo")]
    [InlineData("LenovoFake")]
    [InlineData("Lenovo\n")]
    [InlineData("Lenovo\u202E Group")]
    [InlineData("Lenovo\u00A0 Group")]
    [InlineData("Dell")]
    [InlineData("HP")]
    public void SpoofedOrOtherVendorsCannotQueryLenovoModule(string manufacturer)
    {
        Assert.False(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(
            Provider(manufacturer)));
    }

    [Fact]
    public void LenovoRejectsOtherIpcProviderAndUnavailableSources()
    {
        Assert.False(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(null));
        Assert.False(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(
            Provider(null)));
        Assert.False(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(
            Provider("Lenovo", detectedCount: 0)));
        Assert.False(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(
            Provider("Lenovo", detectedCount: -1)));
        Assert.False(AdvancedOemCatalogQueryPolicy.CanQueryLenovo(
            Provider("Lenovo", id: "intel")));
    }

    [Fact]
    public void FixedFailureMessagesCannotLeakModuleOrMachinePrivateData()
    {
        foreach (var message in new[]
        {
            AdvancedOemCatalogQueryPolicy.DellUnavailableMessage,
            AdvancedOemCatalogQueryPolicy.LenovoUnavailableMessage
        })
        {
            Assert.Contains("Nessun driver installato", message);
            Assert.DoesNotContain("PRIVATE_", message);
            Assert.DoesNotContain("C:\\", message);
            Assert.DoesNotContain("@", message);
            Assert.DoesNotContain("VEN_", message);
        }
    }
}
