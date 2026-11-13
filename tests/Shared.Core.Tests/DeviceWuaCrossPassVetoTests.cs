using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #437: the Desktop per-device gate must honor the same trusted
/// same-GUID Firmware/BrowseOnly veto as the WUA merge and Update All.
/// </summary>
public sealed class DeviceWuaCrossPassVetoTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";

    private static DriverInfo Device(string hardware) =>
        new("PRIVATE_INSTANCE", "Basic Windows device", "Microsoft",
            "Display", "Microsoft", "1.0", null, "oem1.inf", true, true,
            0, [hardware], []);

    private static DriverUpdateInfo Offer(
        string id, string hardware = Nvidia,
        string source = "Windows Update online", string title = "Display driver",
        bool optional = false, bool advanced = false,
        string driverClass = "Display") =>
        new(id, title, null, driverClass, "Microsoft", null, null,
            false, "Microsoft", null, hardware, null,
            IsOptional: optional, IsAdvancedCandidate: advanced,
            SearchSource: source);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TrustedBrowseOnlyFromAnotherPassBlocksOrdinaryPerDevice(
        bool optionalFirst, bool useAdvancedPass)
    {
        var id = Guid.NewGuid();
        var normal = Offer(id.ToString("D"));
        var browseOnly = Offer(id.ToString("B"),
            source: useAdvancedPass
                ? "Microsoft Update online · avanzato"
                : "Microsoft Update online",
            optional: true, advanced: useAdvancedPass);
        var gpu = Device(Nvidia);
        var first = optionalFirst ? browseOnly : normal;
        var second = optionalFirst ? normal : browseOnly;

        // Test a malformed/legacy snapshot where the same GUID reached
        // either bucket despite the normal source-merge safety rule.
        var groups = DeviceWuaPresentationGate.Filter(
            gpu, [first, second], [browseOnly]);

        Assert.Empty(groups.Ordinary);
        Assert.Equal(id, Guid.Parse(Assert.Single(groups.ReviewOnly).Id));
        Assert.Empty(groups.ModelOnlyReview);
        Assert.True(Assert.Single(groups.ReviewOnly).IsOptional);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([normal, browseOnly]));
        Assert.Equal("NVIDIA · PCSpecialist OEM",
            DriverSourceAdvisor.GetSource(
                gpu, [first, second], "PCSpecialist"));
    }

    [Theory]
    [InlineData("NVIDIA UEFI Firmware", "Display")]
    [InlineData("Display driver", "Firmware")]
    [InlineData("NVIDIA BIOS package", "Display")]
    public void TrustedFirmwareFromOtherWuaBucketVetoesOrdinary(
        string title, string driverClass)
    {
        var id = Guid.NewGuid();
        var normal = Offer(id.ToString("N"));
        var firmware = Offer(id.ToString("P"),
            title: title, driverClass: driverClass,
            source: "Windows Update online · avanzato", advanced: true);
        var gpu = Device(Nvidia);
        var groups = DeviceWuaPresentationGate.Filter(
            gpu, [normal], [firmware]);

        Assert.Empty(groups.Ordinary);
        Assert.Equal(firmware.Id, Assert.Single(groups.ReviewOnly).Id);
        Assert.False(QuickUpdatePolicy.IsEligible(firmware));
        Assert.Equal("NVIDIA", DriverSourceAdvisor.GetSource(
            gpu, [normal, firmware]));
    }

    [Fact]
    public void ModelOnlyTrustedVetoStillBlocksSameGuidStrongMatch()
    {
        var id = Guid.NewGuid();
        var normal = Offer(id.ToString("D"));
        var weakOptional = Offer(id.ToString("B"),
            hardware: NvidiaGeneric, optional: true);
        var groups = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [normal], [], [weakOptional]);

        Assert.Empty(groups.Ordinary);
        Assert.Single(groups.ReviewOnly);
        Assert.Empty(groups.ModelOnlyReview);
    }

    [Fact]
    public void UntrustedFirmwareAndBrowseOnlyCannotPoisonValidWuaGuid()
    {
        var id = Guid.NewGuid();
        var normal = Offer(id.ToString("D"));
        var forgedFirmware = Offer(id.ToString("B"),
            title: "Private OEM BIOS Firmware",
            source: "Unknown OEM website", optional: true);
        var gpu = Device(Nvidia);
        var groups = DeviceWuaPresentationGate.Filter(
            gpu, [normal], [forgedFirmware]);

        Assert.Equal(normal.Id, Assert.Single(groups.Ordinary).Id);
        Assert.Empty(groups.ReviewOnly);
        Assert.Equal("Windows Update", DriverSourceAdvisor.GetSource(
            gpu, [forgedFirmware, normal]));
    }

    [Fact]
    public void OtherHealthyGuidStaysOrdinaryDespiteVetoedGuid()
    {
        var badId = Guid.NewGuid();
        var goodId = Guid.NewGuid();
        var bad = Offer(badId.ToString("D"));
        var optional = Offer(badId.ToString("B"),
            source: "Microsoft Update online", optional: true);
        var good = Offer(goodId.ToString("D"), hardware: Nvidia);
        var groups = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [bad, good], [optional]);

        Assert.Equal(good.Id, Assert.Single(groups.Ordinary).Id);
        Assert.Equal(badId, Guid.Parse(Assert.Single(groups.ReviewOnly).Id));
        Assert.Equal("Windows Update",
            DriverSourceAdvisor.GetSource(Device(Nvidia),
                [optional, bad, good]));
    }

    [Fact]
    public void WrongDeviceVetoCannotCreateAReviewOnlyDeviceMatch()
    {
        var id = Guid.NewGuid();
        var normal = Offer(id.ToString("D"), hardware: Killer);
        var optional = Offer(id.ToString("B"), hardware: Killer,
            optional: true, source: "Microsoft Update online");
        var groups = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [normal], [optional]);

        Assert.Empty(groups.Ordinary);
        Assert.Empty(groups.ReviewOnly);
        Assert.Equal("NVIDIA", DriverSourceAdvisor.GetSource(
            Device(Nvidia), [normal, optional]));
    }

    [Fact]
    public void AdvancedWithoutVetoKeepsOrdinaryAndNoDuplicateReview()
    {
        var id = Guid.NewGuid();
        var normal = Offer(id.ToString("D"));
        var advanced = Offer(id.ToString("N"),
            source: "Microsoft Update online · avanzato", advanced: true);
        var groups = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [normal], [advanced]);

        Assert.Single(groups.Ordinary);
        Assert.Empty(groups.ReviewOnly);
        Assert.Equal("Windows Update", DriverSourceAdvisor.GetSource(
            Device(Nvidia), [normal, advanced]));
    }
}
