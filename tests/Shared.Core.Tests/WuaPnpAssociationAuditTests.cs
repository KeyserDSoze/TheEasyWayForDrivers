using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaPnpAssociationAuditTests
{
    private const string NvidiaSpecific =
        @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string instance, string hardware,
        string? compatible = null) =>
        new(instance, "PRIVATE USER DEVICE", "PRIVATE VENDOR",
            "Display", "PRIVATE PROVIDER", "31.0.0.0", null, "oem123.inf",
            true, true, 0, [hardware],
            compatible is null ? [] : [compatible]);

    private static DriverUpdateInfo Wua(int number, string? hardware,
        IReadOnlyList<string>? additional = null,
        string source = "Windows Update online",
        bool incomplete = false) =>
        new($"00000000-0000-4000-8000-{number:D12}",
            "PRIVATE WINDOWS UPDATE TITLE", null, "Display",
            "PRIVATE PROVIDER", null, null, false, "PRIVATE MANUFACTURER",
            null, hardware, null, SearchSource: source,
            AdditionalHardwareIds: additional,
            AdditionalHardwareIdsIncomplete: incomplete);

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> updates, bool partial = false,
        IReadOnlyList<WuaDriverRelatedSoftware>? software = null) =>
        new(updates,
            partial
                ? [new DriverSearchPassStatus("Windows Update online",
                    "Completed", updates.Count, ""),
                   new DriverSearchPassStatus("Microsoft Update online",
                    "Failed", 0, "PRIVATE USER PATH")]
                : [new DriverSearchPassStatus("Windows Update online",
                    "Completed", updates.Count, "")],
            RelatedSoftware: software);

    [Fact]
    public void NvidiaKillerRealtekCanBeExplainedByFourDistinctGuidCategories()
    {
        var devices = new[]
        {
            Device("PRIVATE_NVIDIA", NvidiaSpecific),
            Device("PRIVATE_KILLER", Killer,
                @"PCI\VEN_8086&DEV_51F1&CC_0280"),
            Device("PRIVATE_REALTEK", Realtek)
        };
        var report = Report([
            Wua(1, NvidiaSpecific),
            Wua(2, @"PCI\VEN_8086&DEV_51F1&CC_0280"),
            Wua(3, NvidiaGeneric, source: "Windows Update online"),
            Wua(4, @"PCI\VEN_1002&DEV_6666"),
            Wua(5, null, incomplete: true)
        ], partial: true, software: [
            new WuaDriverRelatedSoftware(
                "00000000-0000-4000-8000-000000000066",
                "Killer Networking Software", "Windows Update online")
        ]);

        var result = WuaPnpAssociationAudit.Create(devices, report, true);

        Assert.True(result.SearchVerified);
        Assert.True(result.InventoryVerified);
        Assert.True(result.SourcePartial);
        Assert.Equal(5, result.ConfirmedGuids);
        Assert.Equal(1, result.ExactHardware);
        Assert.Equal(1, result.CompatibleOnly);
        Assert.Equal(1, result.GenericModelOnly);
        Assert.Equal(2, result.Unattributed);
        Assert.Equal(1, result.MissingWuaIds);
        Assert.Equal(1, result.IncompleteWuaIds);
        Assert.Equal(result.ConfirmedGuids,
            result.ExactHardware + result.CompatibleOnly +
            result.GenericModelOnly + result.Unattributed);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsOemUpdateProof);
        Assert.Contains("fonti WUA", result.Text);
        Assert.Contains("risultati parziali", result.Text);
        Assert.Contains("solo modello generico", result.Text);
        Assert.DoesNotContain("PRIVATE", result.Text);
        Assert.DoesNotContain("PCI\\", result.Text);
        Assert.DoesNotContain("HDAUDIO\\", result.Text);
    }

    [Fact]
    public void SameGuidAcrossCompletedWuaPassesIsMergedBeforeMatching()
    {
        var id = "00000000-0000-4000-8000-000000000007";
        var first = Wua(7, @"PCI\VEN_8086&DEV_0000",
            source: "Windows configurato");
        var second = Wua(7, Killer,
            source: "Windows Update online", incomplete: true);

        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_KILLER", Killer)],
            Report([first, second]), true);

        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(1, result.ExactHardware);
        Assert.Equal(0, result.Unattributed);
        Assert.Equal(1, result.IncompleteWuaIds);
        Assert.DoesNotContain(id, result.Text);
    }

    [Fact]
    public void SameGuidWithLateRealtekIdInSecondSourceIsNotUnattributed()
    {
        var earlier = Enumerable.Range(1, 1024)
            .Select(i => $@"PCI\VEN_9999&DEV_{i:X4}").ToArray();
        var realtekDevice = Device("PRIVATE_REALTEK", Realtek);
        var result = WuaPnpAssociationAudit.Create(
            [realtekDevice],
            Report([
                Wua(12, @"PCI\VEN_9999&DEV_0000",
                    earlier, source: "Windows configurato"),
                Wua(12, @"PCI\VEN_9999&DEV_FFFE",
                    [Realtek], source: "Windows Update online")
            ]), true);

        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(1, result.ExactHardware);
        Assert.Equal(0, result.Unattributed);
        Assert.Equal(1, result.IncompleteWuaIds);
        Assert.DoesNotContain("PRIVATE", result.Text);
        Assert.DoesNotContain("HDAUDIO", result.Text);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void UnavailableOrFailedSearchDoesNotEmitZeroAvailableOffers()
    {
        var notRun = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_NVIDIA", NvidiaSpecific)], null, true);
        var failed = WuaPnpAssociationAudit.Create(
            [], new DriverSearchReport([], [
                new DriverSearchPassStatus("Windows Update online",
                    "Failed", 0, "private error")
            ]), true);

        Assert.False(notRun.SearchVerified);
        Assert.False(failed.SearchVerified);
        Assert.Contains("ricerca Type=Driver non verificata", notRun.Text);
        Assert.Contains("ricerca Type=Driver non verificata", failed.Text);
        Assert.DoesNotContain("0 offerte confermate", failed.Text);
        Assert.DoesNotContain("0 non attribuite", failed.Text);
    }

    [Fact]
    public void MissingPnpInventoryNeverMeansWuaGuidIsUnattributed()
    {
        var report = Report([Wua(1, Killer)]);
        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_KILLER", Killer)],
            report, inventoryComplete: false);
        Assert.True(result.SearchVerified);
        Assert.False(result.InventoryVerified);
        Assert.Equal(1, result.ConfirmedGuids);
        Assert.Equal(0, result.Unattributed);
        Assert.Contains("inventario PnP non verificato", result.Text);
        Assert.Contains("associazioni non valutabili", result.Text);
        Assert.DoesNotContain("0 non attribuite", result.Text);
    }

    [Fact]
    public void MalformedNonGuidAndTypeSoftwareNeverCountAsDriverOffers()
    {
        var invalid = Wua(1, Killer) with { Id = "PRIVATE NON-GUID" };
        var result = WuaPnpAssociationAudit.Create(
            [Device("PRIVATE_KILLER", Killer)],
            Report([invalid], software: [
                new WuaDriverRelatedSoftware(
                    "00000000-0000-4000-8000-000000000099",
                    "Killer Software", "Windows Update online")
            ]), true);
        Assert.Equal(0, result.ConfirmedGuids);
        Assert.Equal(0, result.ExactHardware);
        Assert.Contains("0 offerte confermate", result.Text);
        Assert.DoesNotContain("PRIVATE", result.Text);
    }

    [Fact]
    public void OverBudgetDevicesOrUpdatesCannotClaimUnmatchedCoverage()
    {
        var devices = Enumerable.Range(0, 2049)
            .Select(i => Device($"PRIVATE_{i}", NvidiaSpecific)).ToArray();
        var report = Report([Wua(1, NvidiaSpecific)]);
        var exceeded = WuaPnpAssociationAudit.Create(devices, report, true);

        Assert.True(exceeded.OverBudget);
        Assert.False(exceeded.InventoryVerified);
        Assert.Contains("oltre il limite", exceeded.Text);
        Assert.Contains("non valutabili", exceeded.Text);
        Assert.DoesNotContain("0 non attribuite", exceeded.Text);
    }

    [Fact]
    public void LargeProductOfBoundedDevicesAndUpdatesIsNowIndexed()
    {
        var devices = Enumerable.Range(0, 500)
            .Select(i => Device($"PRIVATE_{i}", NvidiaSpecific)).ToArray();
        var offers = Enumerable.Range(1, 101)
            .Select(i => Wua(i, Killer)).ToArray();
        var result = WuaPnpAssociationAudit.Create(
            devices, Report(offers), true);

        Assert.False(result.OverBudget);
        Assert.True(result.InventoryVerified);
        Assert.Equal(101, result.ConfirmedGuids);
        Assert.Equal(101, result.Unattributed);
        Assert.Contains("101 non attribuite", result.Text);
        Assert.DoesNotContain("PRIVATE_", result.Text);
    }

    [Fact]
    public void IndexedLargeInventoryPrioritizesExactCompatibleAndWeakModel()
    {
        var devices = Enumerable.Range(0, 500)
            .Select(i => Device($"PRIVATE_{i}",
                $@"PCI\VEN_9999&DEV_{i:X4}")).ToArray();
        devices[^1] = Device("PRIVATE_NVIDIA", NvidiaSpecific);
        devices[^2] = Device("PRIVATE_KILLER",
            @"PCI\VEN_8888&DEV_ABCD", Killer);
        devices[^3] = Device("PRIVATE_REALTEK",
            @"PCI\VEN_10EC&DEV_8125&SUBSYS_12345678");

        var offers = Enumerable.Range(1, 177)
            .Select(i => Wua(i, $@"PCI\VEN_7777&DEV_{i:X4}"))
            .Concat([
                Wua(178, NvidiaSpecific),
                Wua(179, Killer),
                Wua(180, @"PCI\VEN_10EC&DEV_8125")
            ]).ToArray();
        var result = WuaPnpAssociationAudit.Create(
            devices, Report(offers), true);

        Assert.False(result.OverBudget);
        Assert.True(result.InventoryVerified);
        Assert.Equal(180, result.ConfirmedGuids);
        Assert.Equal(1, result.ExactHardware);
        Assert.Equal(1, result.CompatibleOnly);
        Assert.Equal(1, result.GenericModelOnly);
        Assert.Equal(177, result.Unattributed);
        Assert.Equal(180, result.ExactHardware + result.CompatibleOnly +
            result.GenericModelOnly + result.Unattributed);
        Assert.False(result.IsInstallable);
        Assert.DoesNotContain("PRIVATE_", result.Text);
        Assert.DoesNotContain(@"PCI\VEN", result.Text);
    }

    [Fact]
    public void OverBudgetPnpIdentifiersFailClosedWithoutPairwiseWork()
    {
        var device = Device("PRIVATE_MALFORMED", NvidiaSpecific) with
        {
            HardwareIds = Enumerable.Repeat(NvidiaSpecific, 262_145).ToArray()
        };
        var result = WuaPnpAssociationAudit.Create(
            [device], Report([Wua(1, NvidiaSpecific)]), true);

        Assert.True(result.OverBudget);
        Assert.False(result.InventoryVerified);
        Assert.Contains("associazioni non valutabili", result.Text);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void CopiedCoverageReportIsPrivacySafeAndExplainsUnmatchedGuidGap()
    {
        var device = Device("PRIVATE-NVIDIA-INSTANCE", NvidiaSpecific);
        var report = Report([Wua(3, @"PCI\VEN_10EC&DEV_ABCD")]);
        var copy = ScanCoverageText.Create(
            report, [], 1, 0, 0, [device], inventoryComplete: true);

        Assert.Contains("Diagnostica WUA→PnP", copy);
        Assert.Contains("1 non attribuite", copy);
        Assert.Contains("0 con Hardware ID esatto", copy);
        Assert.DoesNotContain("PRIVATE", copy);
        Assert.DoesNotContain("PCI\\VEN_", copy);
        Assert.DoesNotContain("oem123.inf", copy);
        Assert.DoesNotContain("00000000-0000-4000", copy);
    }
}
