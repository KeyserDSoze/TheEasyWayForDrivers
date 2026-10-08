using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class SystemIdentityNormalizerTests
{
    [Theory]
    [InlineData(" To be filled by O.E.M. ")]
    [InlineData("System Product Name")]
    [InlineData("Default string")]
    [InlineData("Unknown")]
    [InlineData("N/A")]
    [InlineData("All Series")]
    [InlineData("System Version")]
    [InlineData("   ")]
    public void Clean_IgnoresPlaceholders(string value) =>
        Assert.Null(SystemIdentityNormalizer.Clean(value));

    [Fact]
    public void Clean_PreservesRealModel() =>
        Assert.Equal("ThinkPad T14 Gen 5", SystemIdentityNormalizer.Clean(" ThinkPad T14 Gen 5 "));

    [Fact]
    public void BuildSupportInstructions_UsesExactModelWhenAvailable() =>
        Assert.Contains("Latitude 7440", SystemIdentityNormalizer.BuildSupportInstructions("Dell", "Latitude 7440"));

    [Fact]
    public void BuildSupportInstructions_RequiresDeviceCompatibilityChecks()
    {
        var message = SystemIdentityNormalizer.BuildSupportInstructions("ASUS", "ROG Zephyrus G14");
        Assert.Contains("Hardware ID", message);
        Assert.Contains("versione del driver", message);
        Assert.Contains("Non installare", message);
    }

    [Fact]
    public void BuildSupportInstructions_DoesNotInventUnknownModel() =>
        Assert.Contains("non è disponibile", SystemIdentityNormalizer.BuildSupportInstructions("Dell", null));

    [Fact]
    public void BuildSupportInstructions_UnknownSystemGivesSafeGuidance() =>
        Assert.Contains("non identificati", SystemIdentityNormalizer.BuildSupportInstructions(null, null));
}
