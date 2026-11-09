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

        public string PrimaryIdText { get; set; } = Nvidia;
        public string TitleText { get; set; } =
            "NVIDIA - Display - 32.0.15.7010";
        public string DescriptionText { get; set; } = "Display driver";
        public string DriverClassText { get; set; } = "Display";
        public string ProviderText { get; set; } = "NVIDIA";
        public string ManufacturerText { get; set; } = "NVIDIA";
        public string ModelText { get; set; } = "RTX 4060";

        public string DriverHardwareID => Get("DriverHardwareID", PrimaryIdText);
        public string Title => Get("Title", TitleText);
        public string Description => Get("Description", DescriptionText);
        public string DriverClass => Get("DriverClass", DriverClassText);
        public string DriverProvider => Get("DriverProvider", ProviderText);
        public long MaxDownloadSize => Get("MaxDownloadSize", 159753L);
        public long MinDownloadSize => Get("MinDownloadSize", 1234L);
        public bool IsDownloaded => Get("IsDownloaded", false);
        public string DriverManufacturer =>
            Get("DriverManufacturer", ManufacturerText);
        public string DriverModel => Get("DriverModel", ModelText);
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

    // Public fixture: dynamic COM-style property binding occurs in the
    // Service assembly, so an internal anonymous type would not be visible.
    public sealed class FirmwareDriver
    {
        public string Title => "NVIDIA - Firmware - UEFI";
        public string Description => "Firmware";
        public string DriverClass => "System";
        public string DriverProvider => "NVIDIA";
        public long MaxDownloadSize => 1024;
        public long MinDownloadSize => 512;
        public bool IsDownloaded => false;
        public string DriverManufacturer => "NVIDIA";
        public string DriverModel => "Platform";
        public DateTime DriverVerDate => new(2026, 10, 1);
        public bool IsHidden => false;
        public bool BrowseOnly => false;
    }

    [Fact]
    public void FirmwareTitleRemainsBlockedEvenIfAllPropertiesAreReadable()
    {
        var firmware = new FirmwareDriver();
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

    [Theory]
    [InlineData("title-huge")]
    [InlineData("title-control")]
    [InlineData("title-bidi")]
    [InlineData("class-huge")]
    [InlineData("class-control")]
    public void MalformedSafetyCriticalWuaTextBecomesReviewOnly(string defect)
    {
        var raw = new ComDriver();
        switch (defect)
        {
            case "title-huge":
                raw.TitleText = new string('N',
                    WuaDriverCandidateReader.MaximumTitleLength + 1);
                break;
            case "title-control":
                raw.TitleText = "NVIDIA\r\nPRIVATE_PC";
                break;
            case "title-bidi":
                raw.TitleText = "NVIDIA\u202E PRIVATE_DEVICE";
                break;
            case "class-huge":
                raw.DriverClassText = new string('D',
                    WuaDriverCandidateReader.MaximumClassLength + 1);
                break;
            default:
                raw.DriverClassText = "Display\0PRIVATE_INSTANCE";
                break;
        }

        var update = Read(raw);
        Assert.True(update.IsAdvancedCandidate);
        Assert.False(QuickUpdatePolicy.IsEligible(update));
        Assert.Equal(Nvidia, update.HardwareId);
        Assert.Equal(Source, update.SearchSource);
        Assert.DoesNotContain("PRIVATE", update.Title);
        Assert.True(update.Title.Length <=
            WuaDriverCandidateReader.MaximumTitleLength);
        if (defect.StartsWith("title", StringComparison.Ordinal))
            Assert.Contains("metadati non verificati", update.Title);
        else
            Assert.Null(update.DriverClass);
    }

    [Fact]
    public void OversizedNonCriticalWuaTextIsDroppedWithoutLosingOrdinaryGuid()
    {
        var raw = new ComDriver
        {
            DescriptionText = new string('A',
                WuaDriverCandidateReader.MaximumDescriptiveTextLength + 1),
            ProviderText = "NVIDIA\nPRIVATE_DRIVER",
            ModelText = new string('M',
                WuaDriverCandidateReader.MaximumVendorTextLength + 1),
            ManufacturerText = "NVIDIA\0PRIVATE_ORDER"
        };
        var update = Read(raw);

        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.Null(update.Description);
        Assert.Null(update.Provider);
        Assert.Null(update.Model);
        Assert.Null(update.Manufacturer);
        Assert.Equal(Nvidia, update.HardwareId);
        Assert.Equal("NVIDIA - Display - 32.0.15.7010", update.Title);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE")]
    [InlineData(@"C:\PRIVATE_USER\driver.inf")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\nPRIVATE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&INVALID/NVIDIA")]
    [InlineData(" ")]
    public void MalformedPrimaryWuaIdDoesNotPreventValidAdditionalId(
        string untrustedId)
    {
        var raw = new ComDriver { PrimaryIdText = untrustedId };
        var primary = WuaDriverCandidateReader.ReadPrimaryHardwareId(raw);
        Assert.Null(primary);

        var candidate = Read(raw, hardware: untrustedId,
            extra: [Nvidia, Killer], incomplete: true);
        Assert.Null(candidate.HardwareId);
        Assert.Equal([Nvidia, Killer], candidate.AdditionalHardwareIds);
        Assert.Equal(Source, candidate.SearchSource);
        Assert.True(QuickUpdatePolicy.IsEligible(candidate));
        Assert.DoesNotContain("PRIVATE", candidate.Title);
    }

    [Fact]
    public void ValidPrimaryAndBoundaryLengthMetadataRemainAccessible()
    {
        var raw = new ComDriver
        {
            PrimaryIdText = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B",
            TitleText = new string('N',
                WuaDriverCandidateReader.MaximumTitleLength),
            DriverClassText = new string('D',
                WuaDriverCandidateReader.MaximumClassLength)
        };

        var id = WuaDriverCandidateReader.ReadPrimaryHardwareId(raw);
        Assert.Equal(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B", id);
        var update = Read(raw, hardware: id);
        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.False(update.IsAdvancedCandidate);
        Assert.Equal(WuaDriverCandidateReader.MaximumTitleLength,
            update.Title.Length);
        Assert.Equal(WuaDriverCandidateReader.MaximumClassLength,
            update.DriverClass?.Length);
    }

}
