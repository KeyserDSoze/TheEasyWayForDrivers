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
    public void DiscoveryFilterFindsNvidiaAudioFromSecondaryWuaEntry()
    {
        var nvidiaAudio = Device() with
        {
            DeviceId = "PRIVATE_NVIDIA_AUDIO",
            HardwareIds = [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"],
            CompatibleIds = []
        };
        var update = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "NVIDIA Audio driver", null, "MEDIA",
            "NVIDIA", null, null, false, "NVIDIA", null,
            @"PCI\VEN_8086&DEV_1111", null,
            AdditionalHardwareIds: [@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"]);

        var discovery = Assert.Single(
            DiscoveryResultAdapter.FromWindowsUpdate([update]));

        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(nvidiaAudio, update));
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(discovery, nvidiaAudio));
        Assert.True(DiscoveryDeviceEvidence.IsDeviceCandidate(discovery, nvidiaAudio));
        Assert.Contains("1 ID aggiuntivo", discovery.Evidence);
        Assert.Contains("Hardware ID esatto",
            DiscoveryDeviceEvidence.Explain(discovery, nvidiaAudio));
    }

    [Fact]
    public void DiscoveryFilterFindsKillerDeviceAfterEntry128WithoutNewPackage()
    {
        var killer = Device() with
        {
            DeviceId = "PRIVATE_KILLER",
            HardwareIds = [@"PCI\VEN_8086&DEV_51F1"],
            CompatibleIds = []
        };
        var extraIds = Enumerable.Range(0, 200)
            .Select(index => $@"PCI\VEN_8086&DEV_{index:X4}").ToArray();
        extraIds[199] = @"PCI\VEN_8086&DEV_51F1";
        var wua = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "Intel network driver", null, "Net",
            "Intel", null, null, false, "Intel", null,
            @"USB\VID_8087&PID_0033", null,
            AdditionalHardwareIds: extraIds);

        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate([wua]));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(killer, wua));
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(result, killer));
        Assert.Equal(wua.Id, result.Identifier);
        Assert.Equal(200, result.AdditionalHardwareIds!.Count);
    }

    [Fact]
    public void MissingPrimaryWuaIdStillMatchesAdditionalHardwareOrCompatibleId()
    {
        var update = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "Intel Bluetooth", null, "Bluetooth",
            "Intel", null, null, false, "Intel", null, null, null,
            AdditionalHardwareIds: [@"PCI\VEN_8086&DEV_1111",
                @"PCI\VEN_8086&DEV_1111&SUBSYS_22223333"]);
        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate([update]));
        Assert.Equal(2, DiscoveryDeviceEvidence.MatchStrength(result, Device()));
        Assert.Contains("ID principale non disponibile", result.Evidence);
        Assert.DoesNotContain("non ha riportato un Hardware ID", result.Evidence);

        var weaker = Device() with
        {
            HardwareIds = [@"PCI\VEN_8086&DEV_FFFF"]
        };
        Assert.Equal(1, DiscoveryDeviceEvidence.MatchStrength(result, weaker));
    }

    [Fact]
    public void IncompleteWuaAssociationsNeverMeanVerifiedAbsence()
    {
        var entries = Enumerable.Range(0, 512)
            .Select(index => $@"PCI\VEN_8086&DEV_{index:X4}")
            .Concat([@"PCI\VEN_8086&DEV_1111&SUBSYS_22223333"])
            .ToArray();
        var update = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "Multi device", null, "Net",
            "Intel", null, null, false, "Intel", null,
            null, null, AdditionalHardwareIds: entries,
            AdditionalHardwareIdsIncomplete: true);
        var result = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate([update]));

        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(Device(), update));
        Assert.Equal(0, DiscoveryDeviceEvidence.MatchStrength(result, Device()));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(result, Device()));
        Assert.True(result.WuaIdsIncomplete);
        Assert.Contains("incompleti",
            DiscoveryDeviceEvidence.Explain(result, Device()));
        Assert.Contains("elenco associazioni WUA incompleto", result.Evidence);
        Assert.Contains("512 ID aggiuntivi", result.Evidence);
    }

    [Fact]
    public void OemCatalogCannotInheritWuaEntriesOrCorrelateByHardwareId()
    {
        var spoofed = new DiscoveryResult(
            DiscoveryResultKind.DellDeploymentPack,
            "Dell", "private", "Package", "1", "Metadata", "Non installabile",
            @"PCI\VEN_8086&DEV_1111",
            [@"PCI\VEN_8086&DEV_1111&SUBSYS_22223333"], true);
        Assert.Equal(0, DiscoveryDeviceEvidence.MatchStrength(spoofed, Device()));
        Assert.False(DiscoveryDeviceEvidence.IsDeviceCandidate(spoofed, Device()));

        var unrelated = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "Generic display", null, "Display",
            "NVIDIA", null, null, false, "NVIDIA", null,
            @"PCI\VEN_10DE&DEV_28A0", null,
            AdditionalHardwareIds: [@"PCI\VEN_8086&DEV_11110"]);
        var wuaResult = Assert.Single(DiscoveryResultAdapter.FromWindowsUpdate([unrelated]));
        Assert.Equal(0, DiscoveryDeviceEvidence.MatchStrength(wuaResult, Device()));
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
