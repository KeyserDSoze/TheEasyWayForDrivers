using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class MicrosoftUpdateCatalogSearchTests
{
    private static DriverInfo Device(
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string>? compatibleIds = null,
        string deviceId = @"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL_ABCD") =>
        new(deviceId, "PRIVATE USER MODEL / RTX 4060 Laptop",
            "Microsoft", "Display", "Microsoft",
            "31.0.15.6000", null, "oem32.inf", true, true, 0,
            hardwareIds, compatibleIds ?? []);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_12345678&REV_A1",
        @"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"pci\ven_8086&dev_51f1&subsys_11118086",
        @"PCI\VEN_8086&DEV_51F1")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&CC_030000",
        @"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"USB\VID_046D&PID_C534&REV_2900&MI_00",
        @"USB\VID_046D&PID_C534")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B&SUBSYS_12345678",
        @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B")]
    [InlineData(@"intelaudio\func_01&ven_10ec&dev_0295",
        @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295")]
    [InlineData(@"SWC\VEN_10DE&DEV_00F4&REV_01",
        @"SWC\VEN_10DE&DEV_00F4")]
    [InlineData(@"ACPI\NVDA0820", @"ACPI\NVDA0820")]
    public void BuildsAnOfficialModelOnlySearchNotAnInstallableCandidate(
        string hardwareId, string expectedModel)
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([hardwareId])));

        Assert.Equal(expectedModel, target.Query);
        Assert.False(target.FromCompatibleId);
        Assert.Equal(Uri.UriSchemeHttps, target.Url.Scheme);
        Assert.Equal(MicrosoftUpdateCatalogSearch.CatalogHost, target.Url.Host);
        Assert.Equal("/Search.aspx", target.Url.AbsolutePath);
        Assert.Equal("q=" + expectedModel,
            Uri.UnescapeDataString(target.Url.Query.TrimStart('?')));
        Assert.False(target.IsInstallable);
        Assert.False(target.CatalogWasQueried);
        Assert.Contains("firma CAT", target.Explanation);
        Assert.DoesNotContain("aggiornamento disponibile", target.Explanation);
    }

    [Fact]
    public void DoesNotExposeFullPnpIdSerialDeviceIdOemSubsystemOrRevision()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"PCI\VEN_10DE&DEV_28A0&SUBSYS_12345678&REV_A1"],
                deviceId: "PRIVATE_SERIAL_123456")));

        var serialized = target.Url.AbsoluteUri;
        Assert.Contains("catalog.update.microsoft.com", serialized);
        Assert.DoesNotContain("PRIVATE", serialized);
        Assert.DoesNotContain("SERIAL", serialized);
        Assert.DoesNotContain("RTX", serialized);
        Assert.DoesNotContain("SUBSYS", serialized);
        Assert.DoesNotContain("12345678", serialized);
        Assert.DoesNotContain("REV_A1", serialized);
        Assert.DoesNotContain("31.0.15.6000", serialized);
        Assert.DoesNotContain("oem32", serialized);
        Assert.DoesNotContain("PRIVATE", target.Query);
    }

    [Fact]
    public void WeakCompatibleIdIsUsedOnlyWhenNoRecognizedHardwareIdExists()
    {
        var fallback = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"ROOT\MEDIA", "bad-without-identifier"],
                [@"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086"])));

        Assert.True(fallback.FromCompatibleId);
        Assert.Equal(@"PCI\VEN_8086&DEV_51F1", fallback.Query);
        Assert.Contains("evidenza più debole", fallback.Explanation);

        var strong = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"PCI\VEN_10DE&DEV_28A0"], [@"PCI\VEN_8086&DEV_51F1"])));
        Assert.False(strong.FromCompatibleId);
        Assert.Equal(@"PCI\VEN_10DE&DEV_28A0", strong.Query);
    }

    [Fact]
    public void FindsRecognizedHardwareIdAfterGenericFirstId()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"ROOT\DISPLAY", @"PCI\VEN_10DE&DEV_28A0"])));
        Assert.Equal(@"PCI\VEN_10DE&DEV_28A0", target.Query);
        Assert.False(target.FromCompatibleId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"PCI\VEN_10DE0&DEV_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A00")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_123")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SERIAL_ABC")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\SERIAL_123")]
    [InlineData(@"C:\Windows\System32\driver.sys")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0/evil")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0?foo=bar")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0%26")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0;USB\VID_046D")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + "\n")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + "\0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + "\u202e")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0" + " ")]
    [InlineData(@"SWC\VEN_10DE0&DEV_00F4")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE")]
    [InlineData(@"INTELAUDIO\FUNC_01&VEN_10EC&DEV_")]
    [InlineData(@"ACPI\NVDA08200")]
    [InlineData(@"USB\VID_046D&PID_C534/../evil")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&REV_A1&UNKNOWN_X")]
    public void RejectsMalformedOrUnrecognizedIdentifiersRatherThanSearching(
        string id)
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device([id])));
    }

    [Fact]
    public void FailsClosedWhenOnlyNameOrInstanceIdHintsAtNvidia()
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [], [], deviceId: @"PCI\VEN_10DE&DEV_28A0\PRIVATE_SERIAL")));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [@"ROOT\DISPLAY"], [@"SWD\MSRRAS"])));
    }

    [Fact]
    public void VeryLongOrNonAsciiInputNeverProducesSearch()
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [@"PCI\VEN_10DE&DEV_28A0" + new string('A', 513)])));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [@"PCI\VEN_10DE&DEV_28A0" + "\u00e9"])));
    }

    [Fact]
    public void UrlContainsOnlyFixedMicrosoftHostAndEncodedQuery()
    {
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"PCI\VEN_10DE&DEV_28A0"])));

        Assert.StartsWith(
            "https://www.catalog.update.microsoft.com/Search.aspx?q=",
            target.Url.AbsoluteUri);
        Assert.Contains("%5C", target.Url.AbsoluteUri);
        Assert.Contains("%26", target.Url.AbsoluteUri);
        Assert.Equal("www.catalog.update.microsoft.com",
            target.Url.DnsSafeHost);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);
    }
}
