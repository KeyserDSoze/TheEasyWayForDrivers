using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DiscoveryResultAdapterTests
{
    [Fact]
    public void DellPack_RemainsDeploymentPackageNotInstallableDriver()
    {
        var entry = Assert.Single(DiscoveryResultAdapter.FromDell([
            new DellDriverPackCatalog.Pack("Latitude 7440", "folder/package.cab", "A04", "WIN11")
        ]));
        Assert.Equal(DiscoveryResultKind.DellDeploymentPack, entry.Kind);
        Assert.Contains("deployment", entry.Guidance);
        Assert.Contains("Latitude 7440", entry.Evidence);
    }

    [Fact]
    public void HpSoftpaq_DoesNotClaimSingleDeviceCompatibility()
    {
        var entry = Assert.Single(DiscoveryResultAdapter.FromHp([
            new HpSoftpaqEntry("sp123", "Audio", "1.2", "Recommended", "2026-01-01")
        ]));
        Assert.Equal("sp123", entry.Identifier);
        Assert.Contains("non verificata", entry.Guidance);
    }

    [Fact]
    public void WindowsUpdate_IdentifiesOwnCatalogAndHardwareEvidence()
    {
        var update = new DriverUpdateInfo(
            "00000000-0000-0000-0000-000000000001", "Adapter", null, null, "Vendor",
            null, null, false, null, null, @"PCI\VEN_8086&DEV_1111", null);
        var entry = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate([update]));
        Assert.Equal(DiscoveryResultKind.WindowsUpdateDriver, entry.Kind);
        Assert.Contains("VEN_8086", entry.Evidence);
        Assert.Equal("Windows Update", entry.Source);
    }

    [Fact]
    public void EmptyInputsProduceEmptyResults()
    {
        Assert.Empty(DiscoveryResultAdapter.FromDell([]));
        Assert.Empty(DiscoveryResultAdapter.FromHp([]));
        Assert.Empty(DiscoveryResultAdapter.FromWindowsUpdate([]));
    }
}
