using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaHidVendorUsageEntryTests
{
    private const string Generic = @"HID\VID_046D&UP:0001_U:0006";
    private const string Logitech = @"HID\VID_046D&PID_C534&MI_00&COL01";
    private const string Microsoft = @"HID\VID_045E&PID_028E";

    public sealed class FakeEntry(string id)
    {
        public string DriverHardwareID => id;
    }

    public sealed class FakeEntries(params string[] ids)
    {
        public int Count => ids.Length;
        public FakeEntry Item(int i) => new(ids[i]);
    }

    public sealed class FakeUpdate(string primary, params string[] ids)
    {
        public string DriverHardwareID => primary;
        public FakeEntries WindowsDriverUpdateEntries { get; } = new(ids);
    }

    private static DriverInfo Device(string hardware) =>
        new(@"HID\PRIVATE_INSTANCE", "PRIVATE HID device", "Microsoft",
            "HIDClass", "Microsoft", "1.0", null, "oem123.inf",
            true, true, 0, [hardware], []);

    private static DriverUpdateInfo Wua(string? primary,
        WindowsDriverEntryIdReader.EntryIds entries) =>
        new("00000000-0000-4000-8000-000000000343",
            "Windows HID device driver", null, "HIDClass", "Microsoft",
            null, null, false, "Microsoft", null, primary, null,
            SearchSource: "Windows Update online",
            AdditionalHardwareIds: entries.AdditionalHardwareIds,
            AdditionalHardwareIdsIncomplete: entries.Truncated);

    [Fact]
    public void StrictUsageInSecondaryEntriesIsRetainedAndNotMarkedIncomplete()
    {
        var com = new FakeUpdate(
            @"HID\VID_045E&PID_028E",
            Generic, Logitech, Generic.ToLowerInvariant());
        var primary = WuaDriverCandidateReader.ReadPrimaryHardwareId(com);
        var entries = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            com, primary);

        Assert.Equal(Microsoft, primary);
        Assert.Equal([Generic, Logitech], entries.AdditionalHardwareIds);
        Assert.False(entries.Truncated);
        Assert.True(PnpIdentifierFallback.IsStrictSingleId(Generic));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(Generic));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(Logitech));

        var candidate = Wua(primary, entries);
        Assert.False(candidate.AdditionalHardwareIdsIncomplete);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Logitech), candidate));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Generic), candidate));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(@"HID\VID_046D&PID_C52B"), candidate));
        var groups = DeviceWuaMatchGroups.Create(Device(Generic), [candidate]);
        Assert.Empty(groups.Ordinary);
        Assert.Empty(groups.ReviewOnly);
        Assert.Empty(groups.ModelOnlyReview!);
        Assert.Equal(candidate.Id, Assert.Single(
            DeviceWuaMatchGroups.Create(Device(Logitech), [candidate])
                .Ordinary).Id);
    }

    [Fact]
    public void PrimaryVendorUsageDeduplicatesSecondaryWithoutFalseTruncation()
    {
        var com = new FakeUpdate(Generic, Generic.ToLowerInvariant());
        var primary = WuaDriverCandidateReader.ReadPrimaryHardwareId(com);
        var entries = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            com, primary);

        Assert.Equal(Generic, primary);
        Assert.Empty(entries.AdditionalHardwareIds);
        Assert.False(entries.Truncated);

        var candidate = Wua(primary, entries);
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(
            Device(Generic), candidate));
        Assert.Empty(DeviceWuaMatchGroups.Create(
            Device(Generic), [candidate]).Ordinary);
        Assert.Empty(DeviceWuaMatchGroups.Create(
            Device(Logitech), [candidate]).Ordinary);

        var report = new DriverSearchReport(
            [candidate],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "Verified WUA pass")]);
        var audit = WuaPnpAssociationAudit.Create(
            [Device(Logitech)], report, inventoryComplete: true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(0, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(0, audit.GenericModelOnly);
        Assert.Equal(1, audit.Unattributed);
        Assert.Equal(0, audit.MissingWuaIds);
        Assert.Equal(0, audit.IncompleteWuaIds);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain(Generic, audit.Text);
        Assert.DoesNotContain("PRIVATE", audit.Text);
        Assert.DoesNotContain(candidate.Id, audit.Text);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE:FAKE&DEV_28A0")]
    [InlineData(@"USB\VID_046D&UP:0001_U:0006")]
    [InlineData(@"HID\VID_046D&UP:0001_U:000")]
    [InlineData(@"HID\VID_046D&UP:0001_U:000G")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006&FAKE_01")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006\PRIVATE_INSTANCE")]
    [InlineData(@"HID\VID_046D&PID_C534&UP:0001_U:0006")]
    [InlineData(@"HID\HID_DEVICE_UP:0001_U:0006")]
    [InlineData(@"C:\PRIVATE_USER\device.inf")]
    [InlineData(@"HID\VID_046D&UP:0001_U:0006 ")]
    public void ColonInOtherIdsCannotBypassStrictParser(
        string invalid)
    {
        var com = new FakeUpdate(Microsoft, invalid, Logitech);
        var entries = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            com, Microsoft);

        Assert.Equal([Logitech], entries.AdditionalHardwareIds);
        Assert.True(entries.Truncated);
        Assert.DoesNotContain("PRIVATE",
            string.Join(" ", entries.AdditionalHardwareIds));
        var candidate = Wua(Microsoft, entries);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(Logitech), candidate));
    }

    [Fact]
    public void UsageAndRealPidAreOneUpdateGuidNotTwoUpdates()
    {
        var com = new FakeUpdate(Generic, Generic, Logitech);
        var primary = WuaDriverCandidateReader.ReadPrimaryHardwareId(com);
        var entries = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
            com, primary);
        Assert.Equal([Logitech], entries.AdditionalHardwareIds);
        Assert.False(entries.Truncated);

        var candidate = Wua(primary, entries);
        var report = new DriverSearchReport(
            [candidate],
            [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "Verified WUA pass")]);
        var audit = WuaPnpAssociationAudit.Create(
            [Device(Logitech)], report, inventoryComplete: true);
        Assert.Equal(1, audit.ConfirmedGuids);
        Assert.Equal(1, audit.ExactHardware);
        Assert.Equal(0, audit.CompatibleOnly);
        Assert.Equal(0, audit.GenericModelOnly);
        Assert.Equal(0, audit.Unattributed);
        Assert.Equal(0, audit.IncompleteWuaIds);
        Assert.False(audit.IsInstallable);
        Assert.DoesNotContain(Logitech, audit.Text);
        Assert.DoesNotContain(Generic, audit.Text);
        Assert.DoesNotContain("PRIVATE", audit.Text);
    }
}
