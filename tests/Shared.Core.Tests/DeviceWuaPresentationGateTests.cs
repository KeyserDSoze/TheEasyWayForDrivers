using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #435: a stale or malformed Desktop IPC snapshot cannot claim a Windows
/// Update offer for the wrong PnP device, even if its GUID is ordinary WUA.
/// </summary>
public sealed class DeviceWuaPresentationGateTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string KillerGeneric = @"PCI\VEN_8086&DEV_51F1";

    private static DriverInfo Device(
        string hardware, IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_INSTANCE", "Generic Windows device", "Microsoft",
            "Display", "Microsoft", "1.0", null, "oem1.inf",
            true, true, 0, [hardware], compatible ?? []);

    private static DriverUpdateInfo Offer(string id, string hardware,
        string source = "Windows Update online",
        bool advanced = false, bool hidden = false,
        string title = "Windows driver") =>
        new(id, title, null, "Display", "Microsoft", null, null, false,
            "Microsoft", null, hardware, null,
            IsHidden: hidden, IsAdvancedCandidate: advanced,
            SearchSource: source);

    [Fact]
    public void ExactGpuAndCompatibleKillerRemainOrdinaryWua()
    {
        var gpu = Offer(Guid.NewGuid().ToString("D"), Nvidia);
        var wifi = Offer(Guid.NewGuid().ToString("D"), KillerGeneric);
        Assert.Equal(gpu.Id, Assert.Single(
            DeviceWuaPresentationGate.Filter(Device(Nvidia), [gpu])
                .Ordinary).Id);
        Assert.Equal(wifi.Id, Assert.Single(
            DeviceWuaPresentationGate.Filter(
                Device(Killer, [KillerGeneric]), [wifi]).Ordinary).Id);
    }

    [Fact]
    public void UnrelatedOrdinaryGuidCannotEnableUpdateOrHideChipVendor()
    {
        var gpu = Device(Nvidia);
        var unrelated = Offer(Guid.NewGuid().ToString("D"), Killer);
        var result = DeviceWuaPresentationGate.Filter(gpu, [unrelated]);

        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview);
        Assert.Equal("NVIDIA · PCSpecialist OEM",
            DriverSourceAdvisor.GetSource(gpu, [unrelated], "PCSpecialist"));
        Assert.Equal("Windows Update",
            DriverSourceAdvisor.GetSource(gpu,
                [Offer(Guid.NewGuid().ToString("D"), Nvidia)],
                "PCSpecialist"));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\CC_0300")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\INSTANCE")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\u00A0")]
    public void GenericOrMalformedWuaIdsCannotBecomeDeviceOffers(string id)
    {
        var gpu = Device(id);
        var update = Offer(Guid.NewGuid().ToString("D"), id);
        Assert.Empty(DeviceWuaPresentationGate.Filter(gpu, [update]).Ordinary);
        Assert.NotEqual("Windows Update",
            DriverSourceAdvisor.GetSource(gpu, [update]));
    }

    [Fact]
    public void FirmwareHiddenAdvancedAndUntrustedSourcesAreNeverOrdinary()
    {
        var gpu = Device(Nvidia);
        var normalId = Guid.NewGuid().ToString("D");
        var offers = new[]
        {
            Offer(normalId, Nvidia, title: "BIOS update"),
            Offer(Guid.NewGuid().ToString("D"), Nvidia, hidden: true),
            Offer(Guid.NewGuid().ToString("D"), Nvidia, advanced: true,
                source: "Windows Update online · avanzato"),
            Offer(Guid.NewGuid().ToString("D"), Nvidia,
                source: "Unverified OEM source"),
            Offer("not-a-guid", Nvidia)
        };
        Assert.Empty(DeviceWuaPresentationGate.Filter(gpu, offers).Ordinary);
        Assert.Equal("NVIDIA", DriverSourceAdvisor.GetSource(gpu, offers));
    }

    [Fact]
    public void ExactAdvancedAndFirmwareAreReviewOnlyNotActionable()
    {
        var gpu = Device(Nvidia);
        var advanced = Offer(Guid.NewGuid().ToString("D"), Nvidia,
            "Windows Update online · avanzato", advanced: true);
        var firmware = Offer(Guid.NewGuid().ToString("D"), Nvidia,
            title: "NVIDIA Firmware");
        var fake = Offer(Guid.NewGuid().ToString("D"), Nvidia,
            source: "Unverified website");
        var wrong = Offer(Guid.NewGuid().ToString("D"), Killer,
            advanced: true, source: "Windows Update online · avanzato");
        var result = DeviceWuaPresentationGate.Filter(
            gpu, [], [advanced, firmware, fake, wrong]);

        Assert.Empty(result.Ordinary);
        Assert.Equal(2, result.ReviewOnly.Count);
        Assert.Contains(advanced, result.ReviewOnly);
        Assert.Contains(firmware, result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview);
    }

    [Fact]
    public void GenericWuaModelIsOnlyReviewNotOrdinary()
    {
        var gpu = Device(Nvidia);
        var generic = Offer(Guid.NewGuid().ToString("D"), NvidiaGeneric);
        var result = DeviceWuaPresentationGate.Filter(
            gpu, [generic], [], [generic]);

        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Equal(generic.Id, Assert.Single(result.ModelOnlyReview).Id);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
    }

    [Fact]
    public void WrongGenericModelUntrustedOriginAndCompatibleAreNotWeakHints()
    {
        var gpu = Device(Nvidia, [NvidiaGeneric]);
        var generic = Offer(Guid.NewGuid().ToString("D"), NvidiaGeneric);
        var unrelated = Offer(Guid.NewGuid().ToString("D"), KillerGeneric);
        var untrusted = Offer(Guid.NewGuid().ToString("D"), NvidiaGeneric,
            "Untrusted OEM site");
        var result = DeviceWuaPresentationGate.Filter(
            gpu, [], [], [generic, unrelated, untrusted]);
        Assert.Empty(result.ModelOnlyReview);

        var missingCompat = Device(Nvidia);
        Assert.Equal(generic.Id,
            Assert.Single(DeviceWuaPresentationGate.Filter(
                missingCompat, [], [], [generic, unrelated, untrusted])
                .ModelOnlyReview).Id);
    }

    [Fact]
    public void IdenticalGuidsInDifferentTextFormsCountOncePerDevice()
    {
        var id = Guid.NewGuid();
        var duplicate = new[]
        {
            Offer(id.ToString("B"), Nvidia),
            Offer(id.ToString("N"), Nvidia),
            Offer(id.ToString("D"), Nvidia)
        };
        var result = DeviceWuaPresentationGate.Filter(Device(Nvidia), duplicate);
        Assert.Single(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
    }

    [Fact]
    public void NoDeclaredIdsCannotClaimAnAvailableUpdate()
    {
        var empty = Offer(Guid.NewGuid().ToString("D"), "");
        Assert.Empty(DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [empty]).Ordinary);
    }

    [Theory]
    [InlineData("B", false)]
    [InlineData("N", true)]
    [InlineData("P", false)]
    public void FullConfirmedReportVetoTurnsExactDeviceOfferIntoReviewOnly(
        string guidFormat, bool firmwareVeto)
    {
        var id = Guid.NewGuid();
        var ordinary = Offer(id.ToString("D"), Nvidia);
        var blocked = firmwareVeto
            ? Offer(id.ToString(guidFormat), Nvidia,
                "Microsoft Update online", title: "NVIDIA Firmware")
            : Offer(id.ToString(guidFormat), Nvidia,
                "Microsoft Update online") with { IsOptional = true };
        var approved = QuickUpdatePolicy.AllOrdinaryIds([ordinary, blocked])
            .Select(Guid.Parse).ToHashSet();

        Assert.Empty(approved);
        // Only the ordinary merged representation reached the device:
        // the veto still applies and PnP evidence is retained for review.
        var view = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [ordinary],
            approvedSourceGuids: approved);
        Assert.Empty(view.Ordinary);
        Assert.Equal(ordinary.Id, Assert.Single(view.ReviewOnly).Id);
        Assert.Empty(view.ModelOnlyReview);
    }

    [Fact]
    public void FullReportAllowsVerifiedHardwareAndCompatibleMatches()
    {
        var gpu = Offer(Guid.NewGuid().ToString("N"), Nvidia);
        var wifi = Offer(Guid.NewGuid().ToString("B"), KillerGeneric);
        var permitted = QuickUpdatePolicy.AllOrdinaryIds([gpu, wifi])
            .Select(Guid.Parse).ToHashSet();

        var gpuView = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [gpu], approvedSourceGuids: permitted);
        var wifiView = DeviceWuaPresentationGate.Filter(
            Device(Killer, [KillerGeneric]), [wifi],
            approvedSourceGuids: permitted);
        Assert.Equal(gpu.Id, Assert.Single(gpuView.Ordinary).Id);
        Assert.Equal(wifi.Id, Assert.Single(wifiView.Ordinary).Id);
        Assert.Empty(gpuView.ReviewOnly);
        Assert.Empty(wifiView.ReviewOnly);
    }

    [Fact]
    public void ForeignSourceDoesNotVetoApprovedWuaDeviceGuid()
    {
        var id = Guid.NewGuid();
        var trusted = Offer(id.ToString("D"), Nvidia);
        var fake = Offer(id.ToString("P"), Nvidia,
            "Untrusted OEM portal", title: "BIOS Firmware")
            with { IsOptional = true };
        var approved = QuickUpdatePolicy.AllOrdinaryIds([trusted, fake])
            .Select(Guid.Parse).ToHashSet();

        Assert.Contains(id, approved);
        var device = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [trusted], [fake],
            approvedSourceGuids: approved);
        Assert.Single(device.Ordinary);
        Assert.Empty(device.ReviewOnly);
        Assert.Empty(device.ModelOnlyReview);
    }

    [Fact]
    public void FullReportVetoNeverPromotesGenericModelHintToInstall()
    {
        var id = Guid.NewGuid();
        var generic = Offer(id.ToString("D"), NvidiaGeneric);
        var approved = new HashSet<Guid> { id };
        var noExactMatch = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [generic], [],
            [generic], approved);
        Assert.Empty(noExactMatch.Ordinary);
        Assert.Empty(noExactMatch.ReviewOnly);
        Assert.Equal(id.ToString("D"),
            Assert.Single(noExactMatch.ModelOnlyReview).Id);
        var denied = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [generic], [],
            [generic], new HashSet<Guid>());
        Assert.Empty(denied.Ordinary);
        Assert.Equal(id.ToString("D"),
            Assert.Single(denied.ModelOnlyReview).Id);
    }

    [Fact]
    public void VetoedGuidDoesNotMaskAnotherIndependentOrdinaryDeviceOffer()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var denied = Offer(idA.ToString("D"), Nvidia);
        var allowed = Offer(idB.ToString("N"), Nvidia);
        var permitted = new HashSet<Guid> { idB };
        var result = DeviceWuaPresentationGate.Filter(
            Device(Nvidia), [denied, allowed],
            approvedSourceGuids: permitted);

        Assert.Equal(allowed.Id, Assert.Single(result.Ordinary).Id);
        Assert.Equal(denied.Id, Assert.Single(result.ReviewOnly).Id);
    }

}
