using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Synthetic late-bound COM objects: a property throwing before an old
/// SafeString(value) would fail the entire WUA pass. Identity and collection
/// enumeration remain mandatory and are tested by the independent pass tests.
/// </summary>
public sealed class WuaDriverCandidateReaderTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Source = "Windows Update online";

    public sealed class ComDriver
    {
        public HashSet<string> Fails { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        private T Get<T>(string field, T value)
        {
            if (Fails.Contains(field))
                throw new InvalidOperationException(
                    "Synthetic COM property getter failure: " + field);
            return value;
        }

        public string DriverHardwareID => Get("DriverHardwareID", Nvidia);
        public string Title => Get("Title", "NVIDIA - Display - 32.0.15.7010");
        public string Description => Get("Description", "Display driver");
        public string DriverClass => Get("DriverClass", "Display");
        public string DriverProvider => Get("DriverProvider", "NVIDIA");
        public long MaxDownloadSize => Get("MaxDownloadSize", 159753L);
        public long MinDownloadSize => Get("MinDownloadSize", 1234L);
        public bool IsDownloaded => Get("IsDownloaded", false);
        public string DriverManufacturer => Get("DriverManufacturer", "NVIDIA");
        public string DriverModel => Get("DriverModel", "RTX 4060");
        public DateTime DriverVerDate => Get("DriverVerDate",
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        public bool IsHidden => Get("IsHidden", false);
        public bool BrowseOnly => Get("BrowseOnly", false);
    }

    public sealed class CancellationDriver
    {
        public string DriverHardwareID =>
            throw new OperationCanceledException("Canceled by user");
    }

    private static TheEasyWayForDrivers.Core.Models.DriverUpdateInfo Read(
        object update, bool advanced = false,
        string? hardware = Nvidia,
        IReadOnlyList<string>? extra = null, bool incomplete = false) =>
        WuaDriverCandidateReader.Read(
            update, Guid.NewGuid().ToString("D"), hardware,
            extra ?? [], incomplete, advanced, Source);

    [Fact]
    public void FullyReadableDriverPreservesOrdinaryWuaOfferAndAllMetadata()
    {
        var raw = new ComDriver();
        var primary = WuaDriverCandidateReader.ReadPrimaryHardwareId(raw);
        Assert.Equal(Nvidia, primary);
        var update = Read(raw, hardware: primary,
            extra: [Killer], incomplete: true);

        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.False(update.IsAdvancedCandidate);
        Assert.False(update.IsHidden);
        Assert.False(update.IsOptional);
        Assert.Equal(159753L, update.SizeBytes);
        Assert.Equal("NVIDIA", update.Provider);
        Assert.Equal("RTX 4060", update.Model);
        Assert.Equal("Display", update.DriverClass);
        Assert.NotNull(update.DriverDate);
        Assert.Equal(Nvidia, update.HardwareId);
        Assert.Equal([Killer], update.AdditionalHardwareIds);
        Assert.True(update.AdditionalHardwareIdsIncomplete);
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("DriverProvider")]
    [InlineData("DriverManufacturer")]
    [InlineData("DriverModel")]
    [InlineData("MaxDownloadSize")]
    [InlineData("MinDownloadSize")]
    [InlineData("DriverVerDate")]
    [InlineData("IsDownloaded")]
    public void NonCriticalGetterFailureDoesNotInvalidateOrdinaryDriver(
        string field)
    {
        var raw = new ComDriver();
        raw.Fails.Add(field);
        var update = Read(raw);
        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.False(update.IsAdvancedCandidate);
        Assert.Equal(Nvidia, update.HardwareId);
        Assert.Equal("NVIDIA - Display - 32.0.15.7010", update.Title);
        if (field == "MaxDownloadSize")
            Assert.Equal(1234L, update.SizeBytes);
        if (field == "DriverVerDate")
            Assert.Null(update.DriverDate);
    }

    [Fact]
    public void UnreadablePrimaryHardwareIdDoesNotDestroyAdditionalWuaEntries()
    {
        var raw = new ComDriver();
        raw.Fails.Add("DriverHardwareID");
        var primary = WuaDriverCandidateReader.ReadPrimaryHardwareId(raw);
        Assert.Null(primary);
        var candidate = Read(raw, hardware: primary,
            extra: [Nvidia, Killer], incomplete: true);
        Assert.Equal(2, candidate.AdditionalHardwareIds!.Count);
        Assert.True(candidate.AdditionalHardwareIdsIncomplete);
        Assert.True(QuickUpdatePolicy.IsEligible(candidate));
        // It remains a single WUA Update GUID, not two installable drivers.
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("DriverClass")]
    [InlineData("IsHidden")]
    [InlineData("BrowseOnly")]
    public void UnreadableSecurityRelevantGetterIsReviewOnly(
        string field)
    {
        var raw = new ComDriver();
        raw.Fails.Add(field);
        var update = Read(raw);

        Assert.True(update.IsAdvancedCandidate);
        Assert.False(QuickUpdatePolicy.IsEligible(update));
        Assert.Equal(Source, update.SearchSource);
        if (field == "IsHidden")
            Assert.True(update.IsHidden);
        if (field == "BrowseOnly")
            Assert.True(update.IsOptional);
        if (field == "Title")
            Assert.Contains("metadati non verificati", update.Title);
    }

    [Fact]
    public void AdvancedAndHiddenDriversCannotBecomeOrdinaryAfterFallback()
    {
        var raw = new ComDriver();
        var advanced = Read(raw, advanced: true);
        Assert.True(advanced.IsAdvancedCandidate);
        Assert.False(QuickUpdatePolicy.IsEligible(advanced));

        raw.Fails.Add("IsHidden");
        var unknownHidden = Read(raw);
        Assert.True(unknownHidden.IsHidden);
        Assert.False(QuickUpdatePolicy.IsEligible(unknownHidden));
    }

    [Fact]
    public void FirmwareTitleRemainsBlockedEvenIfAllPropertiesAreReadable()
    {
        var raw = new ComDriver();
        // A distinct synthetic COM object with literal firmware metadata.
        var firmware = new
        {
            Title = "NVIDIA - Firmware - UEFI",
            Description = "Firmware",
            DriverClass = "System",
            DriverProvider = "NVIDIA",
            MaxDownloadSize = 1024L,
            MinDownloadSize = 512L,
            IsDownloaded = false,
            DriverManufacturer = "NVIDIA",
            DriverModel = "Platform",
            DriverVerDate = DateTime.UtcNow,
            IsHidden = false,
            BrowseOnly = false
        };
        var candidate = Read(firmware);
        Assert.False(QuickUpdatePolicy.IsEligible(candidate));
        Assert.True(QuickUpdatePolicy.IsFirmwareOrBios(
            candidate.Title, candidate.DriverClass));
    }

    [Fact]
    public void CancellationAndNullInputsAreNotSwallowed()
    {
        Assert.Throws<OperationCanceledException>(() =>
            WuaDriverCandidateReader.ReadPrimaryHardwareId(new CancellationDriver()));
        Assert.Throws<ArgumentNullException>(() =>
            WuaDriverCandidateReader.ReadPrimaryHardwareId(null!));
        Assert.Throws<ArgumentNullException>(() =>
            WuaDriverCandidateReader.Read(
                new ComDriver(), Guid.NewGuid().ToString("D"), Nvidia,
                null!, false, false, Source));
    }

    [Fact]
    public void IndividualGetterFailureCannotChangeWuaSearchGuid()
    {
        var guid = Guid.NewGuid().ToString("D");
        var raw = new ComDriver();
        raw.Fails.Add("DriverVerDate");
        var candidate = WuaDriverCandidateReader.Read(
            raw, guid, Nvidia, [Killer], false, false, Source);
        Assert.Equal(guid, candidate.Id);
        Assert.Equal(Killer, Assert.Single(candidate.AdditionalHardwareIds!));
        Assert.True(QuickUpdatePolicy.IsEligible(candidate));
    }
}
