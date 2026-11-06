using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class VendorDriverEvidenceFairnessTests
{
    private static DriverInfo Device(
        string instance, string name, string deviceClass, string hardwareId,
        uint problem = 0) =>
        new(instance, name, "Microsoft", deviceClass,
            "Microsoft", "31.0.1.2", null, "oem9.inf", true,
            true, problem, [hardwareId], []);

    private static DriverUpdateInfo Update(string id, string hardwareId) =>
        new(id, "Synthetic driver", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, hardwareId, null);

    private static DriverInfo[] LargeInventory()
    {
        var intel = Enumerable.Range(1, 180)
            .Select(i => Device("INTEL_LOCAL_" + i, "Network Controller",
                "Net", $@"PCI\VEN_8086&DEV_{i:X4}"));
        var nvidia = new[]
        {
            Device("PRIVATE_NVIDIA_GPU", "Microsoft Basic Display Adapter",
                "Display", @"PCI\VEN_10DE&DEV_28A0"),
            Device("PRIVATE_NVIDIA_AUDIO", "High Definition Audio Device",
                "MEDIA", @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B"),
            Device("PRIVATE_NVIDIA_PLATFORM", "System Controller",
                "System", @"ACPI\NVDA0820")
        };
        var killer = Device("PRIVATE_KILLER", "Killer Wi-Fi 6E AX1675i",
            "Net", @"PCI\VEN_8086&DEV_51F1");
        var amd = Device("PRIVATE_AMD", "Microsoft Basic Display Adapter",
            "Display", @"PCI\VEN_1002&DEV_744C");
        return intel.Concat(nvidia).Append(killer).Append(amd).ToArray();
    }

    [Fact]
    public void IntelDominatedInventoryDoesNotSuppressNvidiaGpuAudioPlatform()
    {
        var rows = VendorDriverEvidenceFinder.Find(LargeInventory(), []);

        Assert.Equal(50, rows.Count);
        Assert.Equal(3, rows.Count(row => row.Vendor == "NVIDIA"));
        Assert.Contains(rows, row => row.Vendor == "NVIDIA" &&
            row.Device == "Microsoft Basic Display Adapter");
        Assert.Contains(rows, row => row.Vendor == "NVIDIA" &&
            row.Device == "High Definition Audio Device");
        Assert.Contains(rows, row => row.Vendor == "NVIDIA" &&
            row.Device == "System Controller");
        Assert.Contains(rows, row => row.Vendor == "Intel");
        Assert.Contains(rows, row => row.Vendor == "Killer / Intel");
        Assert.Contains(rows, row => row.Vendor == "AMD");
        Assert.All(rows, row => Assert.Equal(0, row.CorrelatedWuaCandidates));
    }

    [Fact]
    public void VerySmallLimitPrefersRealWuaHardwareCorrelation()
    {
        var inventory = LargeInventory();
        var audio = Update(Guid.NewGuid().ToString("D"),
            @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B");

        var single = Assert.Single(VendorDriverEvidenceFinder.Find(
            inventory, [audio], maximum: 1));
        Assert.Equal("NVIDIA", single.Vendor);
        Assert.Equal("High Definition Audio Device", single.Device);
        Assert.Equal(1, single.CorrelatedWuaCandidates);

        var two = VendorDriverEvidenceFinder.Find(
            inventory, [audio], maximum: 2);
        Assert.Equal(2, two.Count);
        Assert.Contains(two, item => item.Vendor == "NVIDIA");
    }

    [Fact]
    public void DeviceProblemsArePrioritizedWithoutInventingUpdates()
    {
        var healthyIntel = Device("INTEL_HEALTHY",
            "Network Controller", "Net", @"PCI\VEN_8086&DEV_51F1");
        var brokenIntel = Device("INTEL_BROKEN",
            "Network Controller", "Net", @"PCI\VEN_8086&DEV_51F2",
            problem: 28);

        var first = Assert.Single(VendorDriverEvidenceFinder.Find(
            [healthyIntel, brokenIntel], [], maximum: 1));
        Assert.Equal(0, first.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", first.DisplayLine);
        Assert.Equal("31.0.1.2", first.InstalledVersion);
        // With equal titles and versions this property verifies prioritization
        // through a distinct device name in an independent fixture.
        var namedProblem = brokenIntel with { Name = "Wireless Adapter" };
        var priority = Assert.Single(VendorDriverEvidenceFinder.Find(
            [healthyIntel, namedProblem], [], maximum: 1));
        Assert.Equal("Wireless Adapter", priority.Device);
    }

    [Fact]
    public void SameWuaGuidAcrossConfiguredAndDirectPassCountsOnce()
    {
        var device = Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
            "Display", @"PCI\VEN_10DE&DEV_28A0");
        var guid = Guid.NewGuid().ToString("D");
        var one = Update(guid, device.HardwareIds[0]);
        var duplicate = one with
        {
            Id = guid.ToUpperInvariant(),
            Title = "Duplicate WU pass title",
            SearchSource = "Windows Update online"
        };
        var second = Update(Guid.NewGuid().ToString("D"),
            device.HardwareIds[0]);

        var row = Assert.Single(VendorDriverEvidenceFinder.Find(
            [device], [one, duplicate, second]));
        Assert.Equal(2, row.CorrelatedWuaCandidates);
        Assert.Contains("2 candidati Windows Update per ID", row.DisplayLine);
    }

    [Fact]
    public void RepeatedPnpDeviceIdNeverConsumesMultipleDisplaySlots()
    {
        var device = Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
            "Display", @"PCI\VEN_10DE&DEV_28A0");
        var duplicate = device with
        {
            DeviceId = device.DeviceId.ToLowerInvariant(),
            Name = "GeForce duplicate entry"
        };
        var rows = VendorDriverEvidenceFinder.Find([device, duplicate], []);
        Assert.Single(rows);
        Assert.Equal("Microsoft Basic Display Adapter", rows[0].Device);
    }

    [Fact]
    public void LimitIsBoundedAndNoVendorMetadataChangesInstallability()
    {
        var inventory = LargeInventory();
        Assert.Empty(VendorDriverEvidenceFinder.Find(inventory, [], maximum: 0));
        Assert.Empty(VendorDriverEvidenceFinder.Find(inventory, [], maximum: -10));
        Assert.Equal(80, VendorDriverEvidenceFinder.Find(
            inventory, [], maximum: 1000).Count);
    }

    [Fact]
    public void PartialWuaSourceWithZeroMatchesCannotClaimCompleteZero()
    {
        var device = Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
            "Display", @"PCI\VEN_10DE&DEV_28A0");
        var row = Assert.Single(VendorDriverEvidenceFinder.Find([device],
            [], wuaWasQueried: true, wuaCoveragePartial: true));
        Assert.True(row.WuaCoveragePartial);
        Assert.Contains("ricerca WUA parziale", row.DisplayLine);
        Assert.Contains("fonti WUA riuscite", row.DisplayLine);
        Assert.DoesNotContain("nessun candidato", row.DisplayLine);
        Assert.Contains("non verificati", row.DisplayLine);
    }

    [Fact]
    public void PartialWuaSourceWithMatchStillDisplaysIncompleteCoverage()
    {
        var device = Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
            "Display", @"PCI\VEN_10DE&DEV_28A0");
        var row = Assert.Single(VendorDriverEvidenceFinder.Find(
            [device], [Update(Guid.NewGuid().ToString("D"), device.HardwareIds[0])],
            wuaCoveragePartial: true));
        Assert.Equal(1, row.CorrelatedWuaCandidates);
        Assert.Contains("Ricerca WUA parziale", row.DisplayLine);
        Assert.Contains("compatibilità da verificare", row.DisplayLine);
    }

    [Fact]
    public void FailedWuaQueriesCannotClaimVerifiedCorrelationEvenWithStaleData()
    {
        var device = Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
            "Display", @"PCI\VEN_10DE&DEV_28A0");
        var stale = Update(Guid.NewGuid().ToString("D"), device.HardwareIds[0]);
        var row = Assert.Single(VendorDriverEvidenceFinder.Find([device],
            [stale], wuaWasQueried: false, wuaCoveragePartial: true));
        Assert.Equal(0, row.CorrelatedWuaCandidates);
        Assert.False(row.WuaCoveragePartial);
        Assert.Contains("ricerca Windows Update non verificata", row.DisplayLine);
        Assert.DoesNotContain("nessun candidato", row.DisplayLine);
    }

    [Fact]
    public void PrivatePnpIdentifiersNeverEnterVersionSummaryOrAddUpdates()
    {
        var device = Device("SECRET_INSTANCE_SERIAL_007",
            "Microsoft Basic Display Adapter", "Display",
            @"PCI\VEN_10DE&DEV_28A0");
        var evidence = Assert.Single(VendorDriverEvidenceFinder.Find([device], []));
        Assert.DoesNotContain("SECRET_INSTANCE", evidence.DisplayLine);
        Assert.DoesNotContain(@"PCI\VEN", evidence.DisplayLine);
        Assert.Equal(0, evidence.CorrelatedWuaCandidates);
    }
}
