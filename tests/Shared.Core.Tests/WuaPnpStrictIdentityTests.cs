using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #418: malformed WMI/IPC PnP identity cannot fabricate exact WUA evidence.
/// </summary>
public sealed class WuaPnpStrictIdentityTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Platform = @"ACPI\NVDA0820";
    private const string GuidId = "00000000-0000-4000-8000-000000000418";

    private static DriverInfo Device(string hardware, string? compatible = null,
        string kind = "Display") =>
        new("PRIVATE_INSTANCE", "Microsoft Basic Device", "NVIDIA",
            kind, "Microsoft", "31.0.0.0", null, "oem123.inf",
            true, true, 0, [hardware],
            compatible is null ? [] : [compatible]);

    private static DriverUpdateInfo Offer(string hardware) =>
        new(GuidId, "NVIDIA driver", null, "Display", "NVIDIA",
            null, null, false, "NVIDIA", null, hardware, null,
            SearchSource: "Windows Update online");

    private static DriverSearchReport Report(string hardware) =>
        new([Offer(hardware)],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_COM_DETAIL", 1)]);

    [Theory]
    [InlineData("PRIVATE")]
    [InlineData("not-an-id")]
    [InlineData(@"C:\Users\PRIVATE\driver.sys")]
    [InlineData(@"ROOT\SOFTWARE\PRIVATE_INSTANCE")]
    [InlineData("ACPI\\NVDA0820\t")]
    [InlineData("ACPI\\NVDA0820\n")]
    public void MalformedOrNonSingleBusTokensCannotCorrelate(string token)
    {
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(token));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(token), Offer(token)));
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\t")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\n")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")]
    public void MalformedLocalHardwareCannotMatchValidWindowsOffer(string local)
    {
        var device = Device(local);
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(Nvidia));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Offer(Nvidia)));
        Assert.Empty(DriverUpdateMatcher.FindMatches(device, [Offer(Nvidia)]));
    }

    [Fact]
    public void MalformedLocalCompatibleIdIsNotAWeakMatch()
    {
        var device = Device(@"PCI\VEN_8086&DEV_1111", Nvidia + " ");
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Offer(Nvidia)));
    }

    [Theory]
    [InlineData(@"ACPI\NVDA0820")]
    [InlineData("  ACPI\\NVDA0820  ")]
    [InlineData(@"ROOT\VIRTUAL_ADAPTER")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"HID\VID_046D&PID_C534")]
    public void ValidWindowsBusModelsRetainExactMatching(string id)
    {
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(id), Offer(id)));
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\n")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\t")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\r")]
    public void AuditCannotTrimWuaControlCharactersIntoAnExactId(string wuaId)
    {
        var device = Device(Nvidia);
        var report = Report(wuaId);
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(wuaId));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, Offer(wuaId)));

        var audit = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        var gpu = ReferenceDriverCoverage.Create([device], report)[5];
        Assert.Equal(1, gpu.Devices);
        Assert.Equal(0, gpu.WindowsDriverCandidates);
    }

    [Fact]
    public void SafeAsciiPaddingStillMatchesInAllViews()
    {
        var device = Device("  " + Nvidia.ToLowerInvariant() + "  ");
        var report = Report("  " + Nvidia + "  ");
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Offer("  " + Nvidia + "  ")));
        var audit = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(1, ReferenceDriverCoverage.Create([device],
            report)[5].WindowsDriverCandidates);
    }

    [Fact]
    public void AllDiagnosticViewsRejectMalformedLocalNvidiaId()
    {
        var device = Device(Nvidia + "\t");
        var report = Report(Nvidia);
        var audit = WuaPnpAssociationAudit.Create([device], report, true);

        Assert.True(audit.SearchVerified);
        Assert.True(audit.InventoryVerified);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(1, audit.Unattributed);
        var gpu = ReferenceDriverCoverage.Create([device], report)[5];
        Assert.Equal(1, gpu.Devices);
        Assert.Equal(0, gpu.WindowsDriverCandidates);

        var copied = ScanCoverageText.Create(report, [], 1, 0, 0,
            [device], inventoryComplete: true);
        Assert.Contains("NVIDIA · GPU: 1 dispositivi", copied);
        Assert.Contains("0 candidati WUA Type=Driver correlati per ID",
            copied);
        Assert.DoesNotContain("PRIVATE_", copied);
        Assert.DoesNotContain(Nvidia, copied);
    }

    [Fact]
    public void ValidNvidiaAcpiPlatformKeepsMatchingAcrossViews()
    {
        var device = Device(Platform, kind: "System");
        var report = Report(Platform);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            device, Offer(Platform)));
        var audit = WuaPnpAssociationAudit.Create([device], report, true);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.Unattributed);
        var platform = ReferenceDriverCoverage.Create([device], report)[7];
        Assert.Equal(1, platform.Devices);
        Assert.Equal(1, platform.WindowsDriverCandidates);
    }
}
