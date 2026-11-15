using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #462: a confirmed WUA GUID with only generic/non-specific PnP IDs is
/// NOT evidence of a missing/newer driver. Diagnose that gap anonymously
/// without merging failed sources or authorizing Windows Update actions.
/// </summary>
public sealed class WuaNonCorrelatableIdAuditTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek =
        @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string instance, string id) =>
        new(instance, "PRIVATE_DEVICE_NAME", "PRIVATE_MANUFACTURER",
            "System", "PRIVATE_PROVIDER", "1.0", null, "oem123.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Offer(int number, string? primary,
        IReadOnlyList<string>? additional = null,
        string source = "Windows Update online",
        bool incomplete = false) =>
        new($"00000000-0000-4000-8000-{number:D12}",
            "PRIVATE WUA TITLE", null, "System", "Microsoft",
            null, null, false, "PRIVATE PUBLISHER",
            null, primary, null, SearchSource: source,
            AdditionalHardwareIds: additional,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> updates,
        IReadOnlyList<DriverSearchPassStatus>? sources = null) =>
        new(updates, sources ?? [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", updates.Count, "", updates.Count)
        ]);

    [Fact]
    public void GenericOnlyOffersExplainUnattributedGuidWithoutInventingUpdates()
    {
        var installed = new[]
        {
            Device("PRIVATE_NVIDIA", Nvidia),
            Device("PRIVATE_KILLER", Killer),
            Device("PRIVATE_REALTEK", Realtek)
        };
        var report = Report([
            Offer(1, @"PCI\CC_030000"),                         // class-only
            Offer(2, @"PCI\VEN_8086"),                         // vendor-only
            Offer(3, @"HDAUDIO\FUNC_01&VEN_10EC"),             // audio function
            Offer(4, Nvidia + "\t"),                          // malformed: removed at merge, marked incomplete
            Offer(5, null),                                   // no WUA ID
            Offer(6, @"PCI\VEN_9999&DEV_7777"),              // specific, no device
            Offer(7, Nvidia)                                  // genuine match
        ]);
        var result = WuaPnpAssociationAudit.Create(installed, report, true);

        Assert.True(result.SearchVerified);
        Assert.True(result.InventoryVerified);
        Assert.Equal(7, result.ConfirmedGuids);
        Assert.Equal(1, result.ExactHardware);
        Assert.Equal(0, result.CompatibleOnly);
        Assert.Equal(0, result.GenericModelOnly);
        Assert.Equal(6, result.Unattributed);
        // An unsafe legacy WUA primary ID cannot remain visible in
        // the merged record. It becomes an unverified missing ID with
        // explicit incomplete coverage, not a valid generic class hint.
        Assert.Equal(2, result.MissingWuaIds);
        Assert.Equal(1, result.IncompleteWuaIds);
        Assert.Equal(3, result.OnlyNonCorrelatableWuaIds);
        Assert.True(result.OnlyNonCorrelatableWuaIds <= result.Unattributed);
        Assert.Equal(result.ConfirmedGuids,
            result.ExactHardware + result.CompatibleOnly +
            result.GenericModelOnly + result.Unattributed);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsOemUpdateProof);
        Assert.Contains("3 GUID WUA dichiarano solo ID generici", result.Text);

        var copy = ScanCoverageText.Create(report, [],
            installed.Length, 0, 0, installed, inventoryComplete: true);
        Assert.Contains("3 GUID WUA dichiarano solo ID generici", copy);
        Assert.Contains("6 non attribuite", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
        Assert.DoesNotContain("PCI\\", copy);
        Assert.DoesNotContain("HDAUDIO\\", copy);
        Assert.DoesNotContain("00000000-0000-4000", copy);
        Assert.DoesNotContain("oem123.inf", copy);
    }

    [Fact]
    public void LateSpecificIdFromSameGuidRemovesGenericOnlyDiagnosis()
    {
        var first = Offer(8, @"PCI\CC_0300",
            source: "Windows configurato");
        var later = Offer(8, @"PCI\VEN_10DE",
            [Nvidia], source: "Windows Update online");
        var report = Report([first, later], [
            new DriverSearchPassStatus("Windows configurato",
                "Completed", 1, "", 1),
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "", 0)
        ]);
        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_GPU", Nvidia)], report, true);

        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(1, result.ExactHardware);
        Assert.Equal(0, result.Unattributed);
        Assert.Equal(0, result.OnlyNonCorrelatableWuaIds);
        Assert.Equal(0, result.MissingWuaIds);
        Assert.DoesNotContain("soli ID generici", result.Text);
    }

    [Fact]
    public void FailedSourceAndUnknownOriginCannotContaminateGuidAudit()
    {
        var completed = Offer(9, @"PCI\VEN_8086");
        var failed = Offer(10, Killer,
            source: "Microsoft Update online");
        var foreign = Offer(11, Realtek,
            source: "Untrusted OEM download");
        var report = Report([completed, failed, foreign], [
            new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "", 1),
            new DriverSearchPassStatus("Microsoft Update online",
                "NetworkError", 0, "PRIVATE_PROXY", 0)
        ]);
        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_KILLER", Killer),
             Device("PRIVATE_AUDIO", Realtek)], report, true);

        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(1, result.Unattributed);
        Assert.Equal(1, result.OnlyNonCorrelatableWuaIds);
        Assert.Equal(0, result.ExactHardware);
        Assert.True(result.SourcePartial);
        Assert.DoesNotContain("PRIVATE_PROXY", result.Text);
    }

    [Fact]
    public void IncompleteTransportIdsNeverProveNoMatchingHardware()
    {
        var report = Report([
            Offer(12, @"USB\VID_8087",
                incomplete: true)
        ]);
        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_BT", @"USB\VID_8087&PID_0033")],
            report, true);

        Assert.Equal(1, result.OnlyNonCorrelatableWuaIds);
        Assert.Equal(1, result.Unattributed);
        Assert.Equal(1, result.IncompleteWuaIds);
        Assert.Contains("nella porzione letta", result.Text);
        Assert.Contains("elenco ID WUA incompleto", result.Text);
        Assert.Contains("non significa driver assente", result.Text);
    }

    [Fact]
    public void WithoutVerifiedInventoryAssociationsRemainUnknown()
    {
        var report = Report([Offer(13, @"PCI\CC_0280")]);
        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_WLAN", Killer)], report, false);

        Assert.True(result.SearchVerified);
        Assert.False(result.InventoryVerified);
        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(0, result.Unattributed);
        Assert.Equal(0, result.OnlyNonCorrelatableWuaIds);
        Assert.Contains("inventario PnP non verificato", result.Text);
        Assert.DoesNotContain("0 non attribuite", result.Text);
    }

    [Fact]
    public void SpecificCompatibleAndReviewModelAreNeverGenericOnly()
    {
        const string oem = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        var device = Device("PRIVATE_GPU", oem) with
        {
            CompatibleIds = [@"PCI\VEN_10DE&DEV_28A0&CC_0300"]
        };
        var report = Report([
            Offer(14, @"PCI\VEN_10DE&DEV_28A0&CC_0300"),
            Offer(15, Nvidia),
            Offer(16, @"PCI\VEN_8086&DEV_8888")
        ]);
        var result = WuaPnpAssociationAudit.Create([device], report, true);

        Assert.Equal(3, result.ConfirmedGuids);
        Assert.Equal(0, result.ExactHardware);
        Assert.Equal(1, result.CompatibleOnly);
        Assert.Equal(1, result.GenericModelOnly);
        Assert.Equal(1, result.Unattributed);
        Assert.Equal(0, result.OnlyNonCorrelatableWuaIds);
        Assert.False(result.IsInstallable);
    }
}
