using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaGenericModelCorrelationTests
{
    private const string NvidiaSpecific =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string KillerSpecific =
        @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
    private const string KillerGeneric = @"PCI\VEN_8086&DEV_51F1";
    private const string AudioSpecific =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&SUBSYS_12345678";
    private const string AudioGeneric =
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";

    private static DriverInfo Device(string model,
        IReadOnlyList<string>? compatibles = null,
        string title = "Generic Microsoft Device",
        IReadOnlyList<string>? hardware = null) =>
        new("PRIVATE\\DEVICE_INSTANCE\\XYZ", title, "Microsoft",
            "Display", "Microsoft", "31.0.15.5000", null, "oem99.inf",
            true, true, 0, hardware ?? [model], compatibles ?? []);

    private static DriverUpdateInfo Update(string model,
        string? id = null,
        string title = "NVIDIA - Display - New release",
        string source = "Windows Update online",
        bool advanced = false,
        IReadOnlyList<string>? additional = null,
        string deviceClass = "Display") =>
        new(id ?? Guid.NewGuid().ToString("D"), title, null,
            deviceClass, "Microsoft", null, 1024, false,
            "Microsoft", "Windows driver entry", model,
            null, IsAdvancedCandidate: advanced,
            SearchSource: source, AdditionalHardwareIds: additional);

    [Fact]
    public void NvidiaSpecificHardwareWithoutCompatibleIdRecoversGenericOnlyAsReview()
    {
        var device = Device(NvidiaSpecific, title: "Microsoft Basic Display Adapter");
        var update = Update(NvidiaGeneric);
        var result = DeviceWuaMatchGroups.Create(device, [update]);

        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, update));
        Assert.True(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(device, update));
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Equal(update.Id, Assert.Single(result.ModelOnlyReview!).Id);
        Assert.Equal(1, result.TotalCorrelated);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(result.Ordinary));
    }

    [Theory]
    [InlineData(KillerSpecific, KillerGeneric, "Killer Wireless")]
    [InlineData(AudioSpecific, AudioGeneric, "Microsoft High Definition Audio")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295&SUBSYS_11445566",
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295", "Intel SST Audio")]
    [InlineData(@"SWC\VEN_10DE&DEV_00F4&SUBSYS_16A217AA",
        @"SWC\VEN_10DE&DEV_00F4", "NVIDIA SoftwareComponent")]
    [InlineData(@"USB\VID_0BDA&PID_C82D&REV_0200",
        @"USB\VID_0BDA&PID_C82D", "Realtek Bluetooth")]
    public void RecognizedModelsMayBeReviewedButNotOneClickInstalled(
        string specific, string generic, string title)
    {
        var device = Device(specific, title: title);
        var update = Update(generic, advanced: true,
            source: "Microsoft Update online · avanzato");
        var groups = DeviceWuaMatchGroups.Create(device, [update]);

        Assert.True(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(device, update));
        Assert.Empty(groups.Ordinary);
        Assert.Empty(groups.ReviewOnly);
        Assert.Equal(update.Id, Assert.Single(groups.ModelOnlyReview!).Id);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(groups.Ordinary));
    }

    [Fact]
    public void ExactHardwareOrExistingCompatibleIdKeepsItsOrdinaryEligibility()
    {
        var exact = Update(NvidiaSpecific);
        var matching = DeviceWuaMatchGroups.Create(Device(NvidiaSpecific), [exact]);
        Assert.Equal(exact.Id, Assert.Single(matching.Ordinary).Id);
        Assert.Empty(matching.ModelOnlyReview!);
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(
            Device(NvidiaSpecific), exact));

        var compatible = DeviceWuaMatchGroups.Create(
            Device(NvidiaSpecific, [NvidiaGeneric]), [Update(NvidiaGeneric)]);
        Assert.Single(compatible.Ordinary);
        Assert.Empty(compatible.ModelOnlyReview!);
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            Device(NvidiaSpecific, [NvidiaGeneric]),
            compatible.Ordinary[0]));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A1")]
    [InlineData(@"PCI\VEN_1002&DEV_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A00")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_12345678")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&REV_02")]
    [InlineData(@"USB\VID_10DE&PID_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0?malformed")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0 ")]
    public void DifferentModelBusOemSubsystemOrUntrustedWuaIdCannotMatch(
        string wrongWuaId)
    {
        var result = DeviceWuaMatchGroups.Create(
            Device(NvidiaSpecific), [Update(wrongWuaId)]);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview!);
        Assert.Equal(0, result.TotalCorrelated);
    }

    [Fact]
    public void HdaudioFuncNumberMismatchNeverCorrelates()
    {
        var result = DeviceWuaMatchGroups.Create(
            Device(AudioSpecific),
            [Update(@"HDAUDIO\FUNC_02&VEN_10DE&DEV_009B")]);
        Assert.Empty(result.ModelOnlyReview!);
    }

    [Fact]
    public void MarketingNameManufacturerOrDeviceInstanceIdAreNeverMatchKeys()
    {
        var device = Device(@"ROOT\DISPLAY", title: "NVIDIA GeForce RTX 4060",
            hardware: [@"ROOT\DISPLAY"]);
        var result = DeviceWuaMatchGroups.Create(device, [Update(NvidiaGeneric)]);
        Assert.Equal(0, result.TotalCorrelated);

        var compatibleOnly = Device(@"ROOT\DISPLAY",
            compatibles: [NvidiaSpecific]);
        Assert.Equal(0, DeviceWuaMatchGroups.Create(
            compatibleOnly, [Update(NvidiaGeneric)]).TotalCorrelated);
    }

    [Fact]
    public void MissingGuidUntrustedSourceFirmwareAndSoftwareWithoutDeviceIdsStayOut()
    {
        var device = Device(NvidiaSpecific);
        var invalid = Update(NvidiaGeneric, id: "not-a-guid");
        var unknown = Update(NvidiaGeneric, source: "Untrusted OEM website");
        var firmware = Update(NvidiaGeneric,
            title: "NVIDIA UEFI Firmware", deviceClass: "Firmware");
        var noId = Update("", title: "NVIDIA GeForce RTX 4060");
        var result = DeviceWuaMatchGroups.Create(device,
            [invalid, unknown, firmware, noId]);

        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Empty(result.ModelOnlyReview!);
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(device, unknown));
        Assert.False(WuaGenericModelCorrelation.IsWeakModelOnlyMatch(device, firmware));
    }

    [Fact]
    public void AdditionalWuaIdsFromOtherDevicesCanCorrelateWithoutPrimary()
    {
        var first = Update(@"PCI\VEN_8086&DEV_1234",
            additional: [NvidiaGeneric],
            source: "Windows configurato · avanzato",
            advanced: true);
        var result = DeviceWuaMatchGroups.Create(Device(NvidiaSpecific), [first]);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.ReviewOnly);
        Assert.Equal(first.Id, Assert.Single(result.ModelOnlyReview!).Id);
    }

    [Fact]
    public void SameGuidCannotBeDuplicatedBetweenExactAndWeakGroups()
    {
        var id = Guid.NewGuid().ToString("D");
        var stronger = Update(NvidiaSpecific, id: id);
        var weaker = Update(NvidiaGeneric, id: id.ToUpperInvariant());
        var groups = DeviceWuaMatchGroups.Create(
            Device(NvidiaSpecific), [weaker, stronger]);
        Assert.True(string.Equals(stronger.Id,
            Assert.Single(groups.Ordinary).Id,
            StringComparison.OrdinalIgnoreCase));
        Assert.Empty(groups.ModelOnlyReview!);
    }

    [Fact]
    public void ManyGenericCandidatesAreBoundedDeterministicallyToSixteen()
    {
        var device = Device(NvidiaSpecific);
        var list = Enumerable.Range(0, 24)
            .Select(i => Update(NvidiaGeneric,
                id: $"00000000-0000-4000-8000-{i + 1:D12}",
                title: $"NVIDIA Display {i:D2}"))
            .Reverse().ToArray();
        var first = DeviceWuaMatchGroups.Create(device, list);
        var second = DeviceWuaMatchGroups.Create(device, list.Reverse());

        Assert.Empty(first.Ordinary);
        Assert.Empty(first.ReviewOnly);
        Assert.Equal(16, first.ModelOnlyReview!.Count);
        Assert.Equal(first.ModelOnlyReview.Select(x => x.Id),
            second.ModelOnlyReview!.Select(x => x.Id));
        Assert.Equal(16, first.TotalCorrelated);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(first.Ordinary));
    }

    [Fact]
    public void DuplicateGuidInTwoPassesCountsOnceForReview()
    {
        var id = Guid.NewGuid().ToString("D");
        var standard = Update(NvidiaGeneric, id: id);
        var advanced = Update(NvidiaGeneric, id: id.ToUpperInvariant(),
            advanced: true, source: "Windows Update online · avanzato");
        var groups = DeviceWuaMatchGroups.Create(Device(NvidiaSpecific),
            [advanced, standard]);
        Assert.Equal(standard.Id, Assert.Single(groups.ModelOnlyReview!).Id);
        Assert.Equal(1, groups.TotalCorrelated);
    }

    [Fact]
    public void ExtraInvalidModelIdsDoNotBecomeVerifiedCatalogQueries()
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(
            @"PCI\VEN_10DE&DEV_28A0&SERIAL_SECRET"));
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(
            @"PCI\VEN_10DE&DEV_28A0\INSTANCE"));
        Assert.Equal(NvidiaGeneric,
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(NvidiaSpecific));
    }
}
