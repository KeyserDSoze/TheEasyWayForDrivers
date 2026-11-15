using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #472: an explicit WUA-only refresh must recompute manual support
/// correlations without silently deleting previously queried official
/// OEM catalog metadata, current-PC recommendations and news. A new
/// full PC scan must still start with an empty OEM collection.
/// </summary>
public sealed class VendorSourceResultRetentionTests
{
    private const string DellUrl = "https://www.dell.com/support/home";
    private const string HpUrl = "https://support.hp.com/";
    private const string LenovoUrl = "https://support.lenovo.com/";

    private static VendorSourceCheck Row(
        string title, string vendor, string officialUrl,
        bool metadata = false, bool applicable = false,
        bool release = false) =>
        new(title, vendor, "Non confrontata", officialUrl,
            "Fonte ufficiale, sola lettura",
            "Risultato OEM non verificato da Windows Update", 0,
            IsCatalogMetadata: metadata, IsVendorApplicable: applicable,
            IsReleaseAnnouncement: release);

    private static VendorSourceCheck Manual(string vendor, string url,
        int wua = 0) => Row("Supporto manuale " + vendor, vendor, url)
        with
        {
            GroupedManualDevices = 3,
            GroupedManualDevicesWithWuaIds = wua
        };

    [Fact]
    public void WuaRefreshPreservesAllThreeCurrentPcCompanionsAndCatalogMetadata()
    {
        var dellDcu = Row("Dell driver consigliato", "Dell", DellUrl,
            metadata: true, applicable: true);
        var hpHpia = Row("HP SoftPaq da valutare", "HP", HpUrl,
            metadata: true, applicable: true);
        var lenovo = Row("Lenovo client update", "Lenovo", LenovoUrl,
            metadata: true, applicable: true);
        var dellCatalog = Row("Dell Driver Pack pubblicato", "Dell", DellUrl,
            metadata: true);
        var hpCatalog = Row("HP catalogo SoftPaq", "HP", HpUrl,
            metadata: true);

        var saved = VendorSourceResultRetention.KeepEvidence([
            Manual("NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl, wua: 2),
            dellDcu, hpHpia, lenovo, dellCatalog, hpCatalog,
            Manual("Killer / Intel", VendorSourceDiscovery.IntelWifiUrl, wua: 1)
        ]);

        Assert.Equal(5, saved.Count);
        Assert.Equal([dellDcu, hpHpia, lenovo, dellCatalog, hpCatalog], saved);
        Assert.All(saved, row =>
        {
            Assert.True(row.IsCatalogMetadata || row.IsVendorApplicable);
            Assert.False(row.IsInstallable);
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
                row.OfficialUrl));
        });
        // The WUA search can have zero confirmed GUIDs: these remain
        // previous OEM evidence, not WUA update/installation offers.
        var noWua = WindowsDriverCandidateMerge.ConfirmedDistinctForReport(
            new DriverSearchReport([], [
                new DriverSearchPassStatus(
                    "Windows Update online", "Completed", 0, "", 0)
            ]));
        Assert.Empty(noWua);
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(noWua));
        // Three companion results (Dell/HP/Lenovo) are separate from
        // the two automatic catalog metadata rows.
        Assert.Equal(3, saved.Count(x => x.IsVendorApplicable));
    }

    [Fact]
    public void CachedManualLinksAreRebuiltFromCurrentWuaAssociations()
    {
        var old = Manual("NVIDIA",
            VendorSourceDiscovery.NvidiaDriversUrl, wua: 3);
        var current = Manual("NVIDIA",
            VendorSourceDiscovery.NvidiaDriversUrl, wua: 0);
        var applicable = Row("Dell DCU", "Dell", DellUrl,
            metadata: true, applicable: true);

        var retained = VendorSourceResultRetention.KeepEvidence([old, applicable]);
        Assert.Equal([applicable], retained);
        var refreshed = new[] { current }.Concat(retained).ToArray();
        Assert.Equal(2, refreshed.Length);
        Assert.Equal(0, refreshed[0].GroupedManualDevicesWithWuaIds);
        Assert.Equal(1, refreshed.Count(x => x.IsVendorApplicable));
        Assert.DoesNotContain(old, refreshed);
        Assert.All(refreshed, x => Assert.False(x.IsInstallable));
    }

    [Fact]
    public void ReleaseAnnouncementIsPreservedButNeverPromotedToOemUpdate()
    {
        var nvidia = Row("NVIDIA GeForce driver article", "NVIDIA",
            VendorSourceDiscovery.NvidiaDriversUrl, release: true);
        var manual = Manual("NVIDIA", VendorSourceDiscovery.NvidiaDriversUrl, 1);
        var saved = VendorSourceResultRetention.KeepEvidence([nvidia, manual]);
        Assert.Equal([nvidia], saved);
        Assert.True(saved[0].IsReleaseAnnouncement);
        Assert.False(saved[0].IsVendorApplicable);
        Assert.False(saved[0].IsInstallable);
    }

    [Fact]
    public void MalformedOrUnknownUrlsNeverSurviveUnrelatedWuaSearch()
    {
        var hostile = new[]
        {
            "https://www.dell.com.evil.example/support/home",
            "http://www.dell.com/support/home",
            "https://www.dell.com/support/home?serial=PRIVATE_ORDER",
            "file:///C:/PRIVATE_DRIVER/driver.inf",
            "https://PRIVATE_OEM_SERVER/driver"
        };
        var rows = hostile.Select(url =>
            Row("PRIVATE_RAW_OEM", "Dell", url, metadata: true,
                applicable: true)).ToArray();
        var trusted = Row("Dell DCU", "Dell", DellUrl,
            metadata: true, applicable: true);
        var saved = VendorSourceResultRetention.KeepEvidence(
            rows.Append(trusted));

        Assert.Equal([trusted], saved);
        Assert.DoesNotContain(saved, x =>
            x.DeviceName.Contains("PRIVATE_", StringComparison.Ordinal));
        Assert.All(saved, x =>
            Assert.True(VendorSourceDiscovery.IsTrustedOfficialUrl(
                x.OfficialUrl)));
    }

    [Fact]
    public void DedupePreviousOemRowsBeforeRepeatedWuaRefresh()
    {
        var first = Row("HP CMSL", "HP", HpUrl, metadata: true);
        var other = Row("HP HPIA", "HP", HpUrl,
            metadata: true, applicable: true);
        var results = VendorSourceResultRetention.KeepEvidence([
            first, first, other, first, other
        ]);
        Assert.Equal([first, other], results);
        var second = VendorSourceResultRetention.KeepEvidence(results);
        Assert.Equal(results, second);
    }

    [Fact]
    public void NewFullPcScanStartsWithNoPreviousOemEvidence()
    {
        var prior = Row("HP HPIA", "HP", HpUrl, metadata: true, applicable: true);
        var displayed = new List<VendorSourceCheck> { prior };
        // MainWindow.RunFullCheckAsync invalidates this collection
        // BEFORE running WUA, PnP or OEM checks for the new inventory.
        displayed.Clear();
        Assert.Empty(VendorSourceResultRetention.KeepEvidence(displayed));
    }

    [Fact]
    public void ArbitraryOrOversizedStoredRowsFailClosed()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VendorSourceResultRetention.KeepEvidence(null!));
        var forged = Manual("HP", HpUrl) with
        {
            IsCatalogMetadata = true,
            IsVendorApplicable = true
        };
        Assert.Empty(VendorSourceResultRetention.KeepEvidence([forged]));
        var many = Enumerable.Range(0,
            VendorSourceResultRetention.MaximumDisplayedRows + 1)
            .Select(n => Row($"HP {n}", "HP", HpUrl, metadata: true));
        Assert.Throws<InvalidDataException>(() =>
            VendorSourceResultRetention.KeepEvidence(many));
    }
}
