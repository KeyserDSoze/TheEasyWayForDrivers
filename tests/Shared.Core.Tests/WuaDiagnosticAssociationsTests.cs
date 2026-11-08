using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaDiagnosticAssociationsTests
{
    private static DriverInfo Device(string instance, string id) =>
        new(instance, "Generic Windows device", "Microsoft",
            "System", "Microsoft", "1.0.0.0", null, "oem17.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Update(string guid, string primary) =>
        new(guid, "Generic WUA driver", null, "System",
            "Microsoft", null, null, false, "Microsoft",
            null, primary, null, SearchSource: "Windows Update online");

    [Fact]
    public void SameGuidDifferentSourcesCoalesceNvidiaPlatformDchAndRealtek()
    {
        const string guid = "00000000-0000-4000-8000-000000000271";
        var first = Update(guid, @"PCI\VEN_9999&DEV_0001");
        var second = first with
        {
            Id = guid.ToUpperInvariant(),
            HardwareId = @"ACPI\NVDA0820",
            AdditionalHardwareIds = [@"SWC\VEN_10DE&DEV_28A0",
                @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295"]
        };
        var result = Assert.Single(WuaDiagnosticAssociations.Create(
            [first, second]));
        Assert.Equal(guid, result.UpdateId);
        Assert.True(result.Matches(Device("PRIVATE_PLATFORM", @"ACPI\NVDA0820")));
        Assert.True(result.Matches(Device("PRIVATE_DCH", @"SWC\VEN_10DE&DEV_28A0")));
        Assert.True(result.Matches(Device("PRIVATE_CODEC",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295")));
        Assert.False(result.Matches(Device("PRIVATE_OTHER", @"ACPI\NVDA0821")));
        Assert.False(result.Matches(Device("PRIVATE_NEARMISS",
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0296")));
    }

    [Fact]
    public void MalformedAndRepeatedGuidsDoNotCreateExtraRows()
    {
        var guid = Guid.NewGuid().ToString("D");
        var offer = Update(guid, @"PCI\VEN_10DE&DEV_28A0");
        var rows = WuaDiagnosticAssociations.Create(
        [
            offer with { Id = "" },
            offer with { Id = " " },
            offer,
            offer with { Id = guid.ToUpperInvariant() }
        ]);
        var row = Assert.Single(rows);
        Assert.True(row.Matches(Device("PRIVATE_GPU", offer.HardwareId!)));
        Assert.Empty(WuaDiagnosticAssociations.Create([]));
    }

    [Fact]
    public void CrossSourceMergeStaysWithin1024IdsPerGuid()
    {
        var guid = Guid.NewGuid().ToString("D");
        var first = Update(guid, @"PCI\VEN_9999&DEV_0000") with
        {
            AdditionalHardwareIds = Enumerable.Range(1, 1024)
                .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray()
        };
        var second = first with
        {
            HardwareId = @"ACPI\NVDA0820",
            AdditionalHardwareIds = [@"SWC\VEN_10DE&DEV_28A0"]
        };
        var row = Assert.Single(WuaDiagnosticAssociations.Create(
            [first, second]));
        Assert.Equal(1024, row.AdditionalHardwareIds?.Count);
        Assert.True(row.Matches(Device("PRIVATE_PLATFORM", @"ACPI\NVDA0820")));
        Assert.True(row.Matches(Device("PRIVATE_DCH", @"SWC\VEN_10DE&DEV_28A0")));
    }
}
