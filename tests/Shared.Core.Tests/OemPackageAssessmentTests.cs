using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemPackageAssessmentTests
{
    [Fact]
    public void DellDeploymentPackIsNotADeviceMatchedDriver()
    {
        var result = OemPackageAssessment.FromDell(
            new DellDriverPackCatalog.Pack("Latitude", "FOLDER/driver.cab", "A02", "Windows 11"));
        Assert.Equal(OemPackageAssessment.Kind.DeploymentPack, result.PackageKind);
        Assert.False(result.DeviceMatchVerified);
        Assert.False(result.SignatureVerified);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void HpAndLenovoDriverMetadataRequireManualReview()
    {
        var hp = OemPackageAssessment.FromHp(
            new HpSoftpaqEntry("sp123", "WiFi", "1", "Recommended", "2026-01-01"));
        var lenovo = OemPackageAssessment.FromLenovo(
            new LenovoCatalogParser.Package("abc", "WiFi", "1", "Networking", "2026-01-01"));
        foreach (var result in new[] { hp, lenovo })
        {
            Assert.Equal(OemPackageAssessment.Kind.DriverMetadata, result.PackageKind);
            Assert.Equal(OemPackageAssessment.Disposition.ManualReview, result.Action);
            Assert.False(result.IsInstallable);
        }
    }

    [Fact]
    public void FirmwareIsExcludedFromGenericDriverFlow()
    {
        var result = OemPackageAssessment.Unknown("Other", "BIOS", true);
        Assert.Equal(OemPackageAssessment.Kind.FirmwareOrBios, result.PackageKind);
        Assert.Equal(OemPackageAssessment.Disposition.ExcludeFromDriverFlow, result.Action);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void UnknownAndHostileLabelsRemainUnverified()
    {
        var result = OemPackageAssessment.Unknown("OEM\nVendor", "x\r\n123", false);
        Assert.Equal("OEMVendor", result.Vendor);
        Assert.Equal("x123", result.PackageId);
        Assert.Equal(OemPackageAssessment.Kind.Unknown, result.PackageKind);
        Assert.False(result.IsInstallable);
    }
}
