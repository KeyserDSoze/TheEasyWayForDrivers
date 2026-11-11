using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A user-consented Microsoft Catalog query is neither an OEM compatibility
/// assertion nor an installable WUA update. Never publish HID instance IDs.
/// </summary>
public sealed class HidCatalogSearchTests
{
    private static DriverInfo Device(
        IReadOnlyList<string> hardwareIds,
        IReadOnlyList<string>? compatibleIds = null) =>
        new(
            @"HID\VID_046D&PID_C534\PRIVATE_SERIAL_AND_INSTANCE",
            "PRIVATE USER MOUSE NAME", "Microsoft", "HIDClass",
            "Microsoft", "12.0", null, "PRIVATE OEM INF",
            true, true, 0, hardwareIds, compatibleIds ?? []);

    [Theory]
    [InlineData(@"HID\VID_046D&PID_C534", @"HID\VID_046D&PID_C534")]
    [InlineData(@"hid\vid_046d&pid_c534&mi_00&col01",
        @"HID\VID_046D&PID_C534")]
    [InlineData(@"HID\VID_046D&PID_C534&REV_0001&MI_00&COL01",
        @"HID\VID_046D&PID_C534")]
    [InlineData(@"HID\VID_1532&PID_0100&IG_01",
        @"HID\VID_1532&PID_0100")]
    [InlineData(@"HID\VID_046D&PID_C534&COL02&MI_01&REV_0002",
        @"HID\VID_046D&PID_C534")]
    public void SpecificHidProductProducesPublicBrowserOnlyCatalogQuery(
        string hardwareId, string expectedModel)
    {
        Assert.True(HidPnpMatchPolicy.IsSpecificHardwareId(hardwareId));
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([hardwareId])));

        Assert.Equal(expectedModel, target.Query);
        Assert.False(target.FromCompatibleId);
        Assert.Equal(expectedModel,
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(hardwareId));
        Assert.Equal(Uri.UriSchemeHttps, target.Url.Scheme);
        Assert.Equal(MicrosoftUpdateCatalogSearch.CatalogHost, target.Url.Host);
        Assert.Equal("/Search.aspx", target.Url.AbsolutePath);
        Assert.Equal("q=" + expectedModel,
            Uri.UnescapeDataString(target.Url.Query.TrimStart('?')));
        Assert.False(target.IsInstallable);
        Assert.False(target.CatalogWasQueried);
        Assert.Contains("firma CAT", target.Explanation);
        Assert.DoesNotContain("aggiornamento disponibile", target.Explanation);

        var fullUrl = Uri.UnescapeDataString(target.Url.AbsoluteUri);
        Assert.DoesNotContain("PRIVATE", fullUrl);
        Assert.DoesNotContain("SERIAL", fullUrl);
        Assert.DoesNotContain("COL0", fullUrl);
        Assert.DoesNotContain("&MI_", fullUrl);
        Assert.DoesNotContain("&REV_", fullUrl);
        Assert.DoesNotContain("&IG_", fullUrl);
        Assert.DoesNotContain("USER MOUSE", fullUrl);
        Assert.DoesNotContain("oem", fullUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpecificHidCompatibleIdIsWeakerAndCannotOverrideHardwareModel()
    {
        const string model = @"HID\VID_046D&PID_C534";
        const string other = @"HID\VID_1532&PID_0100";
        var fallback = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device(
                [@"HID\HID_DEVICE_SYSTEM_MOUSE"], [model])));
        Assert.Equal(model, fallback.Query);
        Assert.True(fallback.FromCompatibleId);
        Assert.Contains("evidenza più debole", fallback.Explanation);

        var stronger = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(Device([other], [model])));
        Assert.Equal(other, stronger.Query);
        Assert.False(stronger.FromCompatibleId);
    }

    [Theory]
    [InlineData(@"HID\HID_DEVICE")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_MOUSE")]
    [InlineData(@"HID\HID_DEVICE_SYSTEM_KEYBOARD")]
    [InlineData(@"HID\VID_046D")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006")]
    [InlineData(@"HID\VID_046D&PID_")]
    [InlineData(@"HID\VID_046D&PID_C53")]
    [InlineData(@"HID\VID_046D&PID_C5340")]
    [InlineData(@"HID\VID_046D&PID_C534&FAKE_01")]
    [InlineData(@"HID\VID_046D&PID_C534&COLG1")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_000")]
    [InlineData(@"HID\VID_046D&PID_C534&REV_01")]
    [InlineData(@"HID\VID_046D&PID_C534&COL01\PRIVATE_SERIAL")]
    [InlineData(@"HID\VID_046D&PID_C534&REV_0001&FAKE_01")]
    [InlineData(@"HID\VID_046D&PID_C534&MI_00/../evil")]
    [InlineData(@"HID\VID_046D&PID_C534?extra=private")]
    [InlineData(@"HID\VID_046D&PID_C534%26SERIAL")]
    [InlineData(@"HID\VID_046D&PID_C534" + "\n")]
    [InlineData(@"HID\VID_046D&PID_C534" + "\0")]
    [InlineData(@"HID\VID_046D&PID_C534" + "\u202e")]
    [InlineData(@"HID\VID_046D&PID_C534" + " ")]
    public void GenericVendorUsagePrivateAndMalformedHidNeverBuildQuery(
        string id)
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device([id])));
    }

    [Fact]
    public void HiddenSerialAndWindowsTitleCannotTurnGenericHidIntoQuery()
    {
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [@"HID\VID_046D&UP:0001_U:0006"])));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(
            [], [])));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([]));
    }

    [Fact]
    public void ModelOnlyCatalogSearchDoesNotGenerateWeakHidWuaOffer()
    {
        const string deviceModel = @"HID\VID_046D&PID_C534&MI_00&COL01";
        const string publicModel = @"HID\VID_046D&PID_C534";
        var device = Device([deviceModel]);
        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(device));
        Assert.Equal(publicModel, target.Query);
        // A same-PID HID variant may legitimately provide a *review-only*
        // WUA hint, but it never authorizes installation or sources from
        // unknown mirrors. Keep that existing behavior unchanged.
        Assert.Contains(publicModel,
            WuaGenericModelCorrelation.ReviewModelKeys(device));
        var external = new DriverUpdateInfo(
            Guid.NewGuid().ToString("D"), "HID driver from external page",
            null, "HIDClass", "Logitech", null, null, false,
            "Logitech", null, publicModel, null,
            SearchSource: "Unknown manufacturer mirror");
        var matches = DeviceWuaMatchGroups.Create(device, [external]);
        Assert.Equal(0, matches.TotalCorrelated);
        Assert.False(QuickUpdatePolicy.IsEligible(external));
        Assert.False(target.IsInstallable);
    }
}
