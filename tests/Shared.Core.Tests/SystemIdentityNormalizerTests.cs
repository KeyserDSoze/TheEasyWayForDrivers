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
    [Theory]
    [InlineData("Dell\\n")]
    [InlineData("Dell\\tInc.")]
    [InlineData("HP\\u202E Inc.")]
    [InlineData("Hewlett-Packard\\u200B")]
    [InlineData("Dell\\u2066")]
    [InlineData("Dell\\u00A0Inc.")]
    [InlineData(@"C:\\Users\\PRIVATE_USER\\Desktop")]
    [InlineData("local-user@example.invalid")]
    [InlineData(@"PCI\\VEN_10DE&DEV_28A0")]
    [InlineData("System SERIALNUMBER123")]
    [InlineData("/home/PRIVATE_USER/model")]
    [InlineData("C:/Users/PRIVATE_USER/model")]
    public void CleanRejectsPrivateOrMalformedWmiIdentityWithoutRepair(
        string raw)
    {
        Assert.Null(SystemIdentityNormalizer.Clean(raw));
    }

    [Fact]
    public void StrictCleanPreservesCommonOemAndPcModelNames()
    {
        foreach (var model in new[]
        {
            "Dell Inc.", "HP Inc.", "Hewlett-Packard",
            "Hewlett Packard", "Latitude 7440",
            "ROG Zephyrus G14", "Recoil 16", "ThinkPad T14 Gen 5"
        })
            Assert.Equal(model, SystemIdentityNormalizer.Clean(" " + model + " "));
    }

}
