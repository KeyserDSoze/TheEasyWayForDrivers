using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #542: vendor installed-version cards must share Home and WUA audit's
/// exact-instance PnP consensus. Duplicates from WMI/SignedDriver do
/// not establish two devices or let one order decide the vendor.
/// </summary>
public sealed class VendorInstalledDuplicatePnpConsensusTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string NvidiaOther = @"PCI\VEN_10DE&DEV_1F15";
    private const string NvidiaGeneric = @"PCI\VEN_10DE&CC_0300";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Killer =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string GuidId =
        "00000000-0000-4000-8000-000000000542";

    private static DriverInfo Device(string id, string hardware,
        string title, string cls) =>
        new(id, title, "Microsoft", cls, "Microsoft",
            "31.0.0.1", null, "oem42.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Wua(string hardware) =>
        new(GuidId, "Windows Type Driver", null, "Display",
            "Microsoft", "31.0.0.2", null, false, "Microsoft",
            null, hardware, null, SearchSource: "Windows Update online");

    [Theory]
    [InlineData(Nvidia, Realtek, "Display", "MEDIA")]
    [InlineData(Nvidia, Killer, "Display", "Net")]
    [InlineData(Killer, Realtek, "Net", "MEDIA")]
    public void ConflictingKnownVendorsNeverCreateInstalledEvidenceInEitherOrder(
        string first, string second, string firstClass, string secondClass)
    {
        var firstRow = Device("PRIVATE_SAME_INSTANCE", first,
            "NVIDIA GeForce RTX 4060", firstClass);
        var secondRow = Device("private_same_instance", second,
            "Killer High Definition Audio", secondClass);
        foreach (var items in new[]
        {
            new[] { firstRow, secondRow },
            new[] { secondRow, firstRow }
        })
        {
            Assert.Empty(PnpDistinctTrustedDevices.Create(items));
            Assert.Empty(VendorDriverEvidenceFinder.Find(
                items, [Wua(first)]));
            Assert.Empty(VendorSourceDiscovery.Find(items, [Wua(first)]));
            var home = HomeVendorCoverageSummary.Create(items,
                null, inventoryComplete: true);
            Assert.All(home, row =>
                Assert.Contains("0 dispositivi PnP", row.DeviceStatus));
        }
    }

    [Fact]
    public void SameVendorWithTwoDifferentPrimaryModelsIsAmbiguous()
    {
        var a = Device("PRIVATE_GPU", Nvidia,
            "NVIDIA GeForce RTX 4060", "Display");
        var b = Device("private_gpu", NvidiaOther,
            "NVIDIA GeForce RTX 4050", "Display");
        Assert.Empty(PnpDistinctTrustedDevices.Create([a, b]));
        Assert.Empty(VendorDriverEvidenceFinder.Find(
            [a, b], [Wua(Nvidia)]));
        Assert.Empty(VendorDriverEvidenceFinder.Find(
            [b, a], [Wua(Nvidia)]));
    }

    [Fact]
    public void GenericFirstCanRecoverSpecificSameInstanceGpuForExactWuaMatch()
    {
        var generic = Device("PRIVATE_GPU", NvidiaGeneric,
            "NVIDIA graphics device", "Unknown");
        var specific = Device("private_gpu", Nvidia,
            "Microsoft Basic Display Adapter", "Display");
        Assert.Same(specific, Assert.Single(
            PnpDistinctTrustedDevices.Create([generic, specific])));
        var rows = VendorDriverEvidenceFinder.Find(
            [generic, specific], [Wua(Nvidia)]);
        var row = Assert.Single(rows);
        Assert.Equal("NVIDIA", row.Vendor);
        Assert.Equal("Microsoft Basic Display Adapter", row.Device);
        Assert.Equal(1, row.CorrelatedWuaCandidates);
        Assert.DoesNotContain("PRIVATE_GPU", row.DisplayLine);
        Assert.Contains("compatibilità da verificare", row.DisplayLine);
    }

    [Fact]
    public void HealthyGpuAndKillerRemainWhenAnotherInstanceIsAmbiguous()
    {
        var badGpu = Device("PRIVATE_BAD", Nvidia,
            "NVIDIA GeForce RTX 4060", "Display");
        var badAudio = Device("private_bad", Realtek,
            "High Definition Audio Device", "MEDIA");
        var good = Device("PRIVATE_KILLER", Killer,
            "Killer Wi-Fi 6E", "Net");
        var rows = VendorDriverEvidenceFinder.Find(
            [badGpu, badAudio, good], [Wua(Nvidia), Wua(Killer)]);
        var kept = Assert.Single(rows);
        Assert.Equal("Killer / Intel", kept.Vendor);
        Assert.Equal(1, kept.CorrelatedWuaCandidates);
        Assert.DoesNotContain("PRIVATE_", kept.DisplayLine);
        Assert.DoesNotContain(Killer, kept.DisplayLine);
        Assert.DoesNotContain(GuidId, kept.DisplayLine);
    }

    [Fact]
    public void ConsistentDuplicateDoesNotCountSameGuidOrDeviceTwice()
    {
        var first = Device("PRIVATE_GPU", Nvidia,
            "NVIDIA GeForce RTX 4060", "Display");
        var duplicate = first with
        {
            DeviceId = "private_gpu", DriverProvider = "NVIDIA",
            DriverVersion = "32.0.0.1"
        };
        var rows = VendorDriverEvidenceFinder.Find(
            [first, duplicate], [Wua(Nvidia)]);
        var one = Assert.Single(rows);
        Assert.Equal("NVIDIA", one.Vendor);
        Assert.Equal(1, one.CorrelatedWuaCandidates);
        Assert.DoesNotContain("PRIVATE_GPU", one.DisplayLine);
    }

    [Fact]
    public void NonCorrelatableGenericWuaTokenDoesNotCreateDeviceMatch()
    {
        var generic = Device("PRIVATE_GPU", NvidiaGeneric,
            "NVIDIA graphics device", "Unknown");
        var actual = Device("private_gpu", Nvidia,
            "Microsoft Basic Display Adapter", "Display");
        var row = Assert.Single(VendorDriverEvidenceFinder.Find(
            [generic, actual], [Wua(NvidiaGeneric)]));
        Assert.Equal(0, row.CorrelatedWuaCandidates);
        Assert.Contains("stato OEM non verificato", row.DisplayLine);
    }
}
