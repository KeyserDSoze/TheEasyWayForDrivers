using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DiscoveryDeviceEvidenceTests
{
    private static DriverInfo Device() =>
        new("PCI\\VEN_8086", "Scheda", "Intel", "System", "Intel", "1.0.0.0",
            null, null, true, true, 0,
            [@"PCI\VEN_8086&DEV_1111&SUBSYS_22223333"],
            [@"PCI\VEN_8086&DEV_1111"]);

    private static DiscoveryResult Result(DiscoveryResultKind kind, string? id = null) =>
        new(kind, "OEM", "pack", "Package", "1.0", "Catalog", "Review", id);

    [Fact]
    public void ExactWuaHardwareIdIsStrongerThanCompatibleId()
    {
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(
            Result(DiscoveryResultKind.WindowsUpdateDriver,
                @"pci\ven_8086&dev_1111&subsys_22223333"), Device()));
        Assert.Equal(1, DiscoveryDeviceEvidence.MatchStrength(
            Result(DiscoveryResultKind.WindowsUpdateDriver,
                @"PCI\VEN_8086&DEV_1111"), Device()));
    }

    [Fact]
    public void OemPackNeverMatchesEvenIfAnIdentifierHappensToBePresent()
    {
        foreach (var kind in new[] {
            DiscoveryResultKind.DellDeploymentPack,
            DiscoveryResultKind.HpSoftpaqDriver
        })
        {
            var result = Result(kind, @"PCI\VEN_8086&DEV_1111");
            Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(result, Device()));
            Assert.Contains("nessuna associazione", DiscoveryDeviceEvidence.Explain(result, Device()));
        }
    }

    [Fact]
    public void UnrelatedWuaDoesNotMatch()
    {
        var result = Result(DiscoveryResultKind.WindowsUpdateDriver,
            @"PCI\VEN_10DE&DEV_1234");
        Assert.Equal(0, DiscoveryDeviceEvidence.MatchStrength(result, Device()));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(result, Device()));
    }

    [Fact]
    public void MissingHardwareIdCannotBeAttributed()
    {
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(
            Result(DiscoveryResultKind.WindowsUpdateDriver), Device()));
    }

    [Fact]
    public void WuaAdapterPreservesActualIdForEvaluation()
    {
        var wua = new DriverUpdateInfo("id", "Intel Update", null, null, "Intel",
            "2.0.0.0", null, false, null, null,
            @"PCI\VEN_8086&DEV_1111", null);
        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate([wua]));
        Assert.Equal(1, DiscoveryDeviceEvidence.MatchStrength(result, Device()));
    }
}
