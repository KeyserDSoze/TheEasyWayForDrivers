using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class AutomaticOemCatalogDisplayTextTests
{
    [Fact]
    public void DellAndHpMessagesContainCountsButNotWmiIdentity()
    {
        var hostileModel = @"C:\Users\PRIVATE_USER\Laptop\driver.inf";
        var hostileVendor = "PRIVATE_SERIALNUMBER_HP";
        var display = new[]
        {
            AutomaticOemCatalogDisplayText.DellCompleted(12),
            AutomaticOemCatalogDisplayText.HpCompleted(60),
            AutomaticOemCatalogDisplayText.UnknownProducer,
            AutomaticOemCatalogDisplayText.QueryFailed
        };
        Assert.Contains("12 pacchetti", display[0]);
        Assert.Contains("60 metadati", display[1]);
        foreach (var status in display)
        {
            Assert.DoesNotContain(hostileModel, status);
            Assert.DoesNotContain(hostileVendor, status);
            Assert.DoesNotContain("PRIVATE_", status);
            Assert.DoesNotContain("C:\\", status);
            Assert.DoesNotContain("@", status);
        }
    }

    [Fact]
    public void InvalidCountsCannotProduceNegativeCatalogNumbers()
    {
        Assert.Contains("0 pacchetti",
            AutomaticOemCatalogDisplayText.DellCompleted(-5));
        Assert.Contains("0 metadati",
            AutomaticOemCatalogDisplayText.HpCompleted(-7));
    }
}
