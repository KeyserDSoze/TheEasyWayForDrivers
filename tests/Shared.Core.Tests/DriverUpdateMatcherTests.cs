using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverUpdateMatcherTests
{
    [Fact]
    public void IsMatch_MatchesExactHardwareIdIgnoringCase()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234"],
            compatibleIds: []);

        var update = CreateUpdate(@"pci\ven_8086&dev_1234");

        Assert.True(DriverUpdateMatcher.IsMatch(driver, update));
    }

    [Fact]
    public void IsMatch_MatchesCompatibleId()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
            compatibleIds: [@"PCI\VEN_8086&DEV_1234"]);

        var update = CreateUpdate(@"PCI\VEN_8086&DEV_1234");

        Assert.True(DriverUpdateMatcher.IsMatch(driver, update));
    }

    [Fact]
    public void IsMatch_DoesNotGuessFromDeviceName()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1111"],
            compatibleIds: []);

        var update = CreateUpdate(@"PCI\VEN_8086&DEV_2222");

        Assert.False(DriverUpdateMatcher.IsMatch(driver, update));
    }

    [Fact]
    public void FindMatches_PrefersExactHardwareIdOverNewerCompatibleId()
    {
        var driver = CreateDriver(
            [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
            [@"PCI\VEN_8086&DEV_1234"]);

        var exact = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234&SUBSYS_00000001",
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Exact");
        var compatible = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Compatible");

        var matches = DriverUpdateMatcher.FindMatches(driver, [compatible, exact]);
        Assert.Equal("Exact", matches[0].Title);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(driver, exact));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(driver, compatible));
    }

    [Fact]
    public void MatchStrength_DoesNotMatchMissingOrDifferentIds()
    {
        var driver = CreateDriver([@"PCI\VEN_8086&DEV_1234"], []);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(driver, CreateUpdate("")));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(driver, CreateUpdate(@"PCI\VEN_8086&DEV_9999")));
    }

    [Fact]
    public void FindMatches_OrdersNewestDriverDateFirst()
    {
        var driver = CreateDriver(
            hardwareIds: [@"PCI\VEN_8086&DEV_1234"],
            compatibleIds: []);

        var older = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Older");

        var newer = CreateUpdate(
            @"PCI\VEN_8086&DEV_1234",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "Newer");

        var matches = DriverUpdateMatcher.FindMatches(
            driver,
            [older, newer]);

        Assert.Equal("Newer", matches[0].Title);
    }

    private static DriverInfo CreateDriver(
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string> compatibleIds) =>
        new(
            @"PCI\VEN_8086&DEV_1234\1",
            "Intel test device",
            "Intel",
            "System",
            "Intel",
            "1.0.0.0",
            DateTimeOffset.UtcNow,
            "test.inf",
            true,
            true,
            0,
            hardwareIds,
            compatibleIds);

    private static DriverUpdateInfo CreateUpdate(
        string hardwareId,
        DateTimeOffset? date = null,
        string title = "Intel update") =>
        new(
            Guid.NewGuid().ToString(),
            title,
            null,
            "System",
            "Intel",
            null,
            1024,
            false,
            "Intel",
            "Test model",
            hardwareId,
            date);
    [Theory]
    [InlineData(@"PCI\CC_0300")]
    [InlineData(@"PCI\CC_030000")]
    [InlineData(@"PCI\CC_0280")]
    [InlineData(@"PCI\VEN_10DE")]
    [InlineData(@"PCI\VEN_10DE&CC_0300")]
    [InlineData(@"PCI\VEN_10DE&FAKE_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SERIAL_PRIVATE")]
    public void PciClassOrIncompleteVendorCannotAuthorizeMatch(string unsafeId)
    {
        var device = CreateDriver(
            [unsafeId, @"PCI\VEN_10DE&DEV_28A0"],
            [unsafeId]);
        var offer = CreateUpdate(unsafeId);
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(unsafeId));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, offer));
        Assert.False(DriverUpdateMatcher.IsMatch(device, offer));
        Assert.Empty(DriverUpdateMatcher.FindMatches(device, [offer]));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&CC_028000")]
    [InlineData(@"USB\VID_8087&PID_0033")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    public void GenuineModelIdsRetainExactAndCompatibleEvidence(string id)
    {
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        var device = CreateDriver([id], [id]);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, CreateUpdate(id)));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            CreateDriver([], [id]), CreateUpdate(id)));
    }


    [Theory]
    [InlineData(@"USB\VID_8087")]
    [InlineData(@"USB\VID_8087&CLASS_02")]
    [InlineData(@"USB\CLASS_02&SUBCLASS_02")]
    [InlineData(@"USB\VID_8087&PID_0033\PRIVATE_INSTANCE")]
    [InlineData(@"USB\VID_8087&PID_0033&FAKE_01")]
    [InlineData(@"HDAUDIO\FUNC_01")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&FAKE_01")]
    [InlineData(@"INTELAUDIO\FUNC_01")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC")]
    [InlineData(@"SWC\VEN_10DE")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0&INVALID_01")]
    public void GenericOrMalformedNonPciIdsCannotCorrelateWindowsDriver(
        string unsafeId)
    {
        var device = CreateDriver([unsafeId], [unsafeId]);
        var offer = CreateUpdate(unsafeId);

        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(unsafeId));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device, offer));
        Assert.Empty(DriverUpdateMatcher.FindMatches(device, [offer]));
    }

    [Theory]
    [InlineData(@"USB\VID_8087&PID_0033")]
    [InlineData(@"USB\VID_0BDA&PID_1234")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"SWC\VEN_10DE&DEV_28A0")]
    [InlineData(@"SWC\VEN_8086&DEV_51F1")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA")]
    [InlineData(@"ACPI\NVDA0820")]
    [InlineData(@"HID\VID_046D&PID_C534")]
    public void SpecificNonPciModelsAndOtherWindowsBusesKeepExactMatching(
        string model)
    {
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(model));
        var offer = CreateUpdate(model);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            CreateDriver([model], []), offer));
        Assert.Equal(1, DriverUpdateMatcher.MatchStrength(
            CreateDriver([], [model]), offer));
    }

    [Fact]
    public void GenericUsbCompatibleIdCannotWinAgainstUnrelatedSpecificOffer()
    {
        const string generic = @"USB\VID_8087";
        var device = CreateDriver(
            [@"USB\VID_8087&PID_0033"], [generic]);
        var genericOffer = CreateUpdate(generic);
        var specificOffer = CreateUpdate(@"USB\VID_8087&PID_0033");
        var matches = DriverUpdateMatcher.FindMatches(device,
            [genericOffer, specificOffer]);

        Assert.Equal(specificOffer.Id, Assert.Single(matches).Id);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            device, genericOffer));
    }

}
