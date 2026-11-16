using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #535: the installed-driver evidence cards must share the incomplete-ID
/// caveat already used in Home vendor coverage and the WUA-PnP audit.
/// Synthetic WUA metadata is not an OEM installation authorization.
/// </summary>
public sealed class VendorInstalledEvidenceIncompleteWuaIdTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Unknown = @"PCI\VEN_9999&DEV_0001";
    private const string GuidA = "00000000-0000-4000-8000-000000000535";
    private const string GuidB = "00000000-0000-4000-8000-000000000536";

    private static DriverInfo Device(string name, string hardware,
        string kind) =>
        new("PRIVATE_INSTANCE", name, "Microsoft", kind, "Microsoft",
            "31.0.0.1", null, "oem12.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Offer(string id, string hardware,
        string source = "Windows Update online", bool incomplete = false) =>
        new(id, "Windows driver", null, "Display", "Microsoft", null, null,
            false, "Microsoft", null, hardware, null,
            SearchSource: source, AdditionalHardwareIdsIncomplete: incomplete);

    private static readonly DriverInfo Gpu =
        Device("Microsoft Basic Display Adapter", Nvidia, "Display");
    private static readonly DriverInfo Audio =
        Device("High Definition Audio Device", Realtek, "MEDIA");
    private static readonly DriverInfo Wifi =
        Device("Network Controller", Killer, "Net");

    [Fact]
    public void TruncatedUnrelatedOfferDoesNotClaimThereAreNoDeviceMatches()
    {
        var updates = new[] { Offer(GuidA, Unknown, incomplete: true) };
        var rows = VendorDriverEvidenceFinder.Find([Gpu, Audio, Wifi], updates);
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.Vendor == "NVIDIA");
        Assert.Contains(rows, row => row.Vendor == "Realtek");
        Assert.Contains(rows, row => row.Vendor == "Killer / Intel");
        Assert.All(rows, row =>
        {
            Assert.True(row.WuaIdsIncomplete);
            Assert.Equal(0, row.CorrelatedWuaCandidates);
            Assert.Contains("0 correlazioni WUA osservate", row.DisplayLine);
            Assert.Contains("assenza non verificabile", row.DisplayLine);
            Assert.DoesNotContain("nessun candidato Windows Update correlato",
                row.DisplayLine);
            Assert.DoesNotContain("PRIVATE_", row.DisplayLine);
            Assert.DoesNotContain(GuidA, row.DisplayLine);
            Assert.DoesNotContain(Unknown, row.DisplayLine);
        });
    }

    [Fact]
    public void ExactGpuMatchIsPreservedAndOtherIdsStayPotentiallyUnknown()
    {
        var rows = VendorDriverEvidenceFinder.Find([Gpu, Audio],
            [Offer(GuidA, Nvidia, incomplete: true)]);
        var gpu = Assert.Single(rows, r => r.Vendor == "NVIDIA");
        var audio = Assert.Single(rows, r => r.Vendor == "Realtek");
        Assert.Equal(1, gpu.CorrelatedWuaCandidates);
        Assert.Contains("1 candidati Windows Update per ID", gpu.DisplayLine);
        Assert.Contains("ulteriori associazioni possibili", gpu.DisplayLine);
        Assert.Equal(0, audio.CorrelatedWuaCandidates);
        Assert.Contains("assenza non verificabile", audio.DisplayLine);
    }

    [Fact]
    public void TwoCompletedSourcesForOneGuidRetainMergedIncompleteBit()
    {
        var rows = VendorDriverEvidenceFinder.Find([Gpu, Audio],
        [
            Offer(GuidA, Nvidia, "Windows configurato"),
            Offer(GuidA, Realtek, incomplete: true)
        ]);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.True(row.WuaIdsIncomplete);
            Assert.Equal(1, row.CorrelatedWuaCandidates);
            Assert.Contains("ulteriori associazioni possibili", row.DisplayLine);
        });
    }

    [Fact]
    public void PartialWuaQueryAndMissingIdsRemainSeparateWarnings()
    {
        var row = Assert.Single(VendorDriverEvidenceFinder.Find([Gpu],
            [Offer(GuidA, Unknown, incomplete: true)],
            wuaCoveragePartial: true));
        Assert.True(row.WuaCoveragePartial);
        Assert.True(row.WuaIdsIncomplete);
        Assert.Contains("ricerca WUA parziale", row.DisplayLine);
        Assert.Contains("ID WUA incompleti", row.DisplayLine);
        Assert.Contains("assenza non verificabile", row.DisplayLine);

        var matched = Assert.Single(VendorDriverEvidenceFinder.Find([Gpu],
            [Offer(GuidA, Nvidia, incomplete: true)],
            wuaCoveragePartial: true));
        Assert.Contains("Ricerca WUA parziale", matched.DisplayLine);
        Assert.Contains("ulteriori associazioni possibili", matched.DisplayLine);
    }

    [Fact]
    public void UnknownOriginCannotFakeIncompleteOfficialWuaEvidence()
    {
        var rows = VendorDriverEvidenceFinder.Find([Gpu, Audio],
        [
            Offer(GuidA, Unknown, "PRIVATE_REMOTE_VENDOR", incomplete: true),
            Offer(GuidB, Nvidia)
        ]);
        var gpu = Assert.Single(rows, r => r.Vendor == "NVIDIA");
        var audio = Assert.Single(rows, r => r.Vendor == "Realtek");
        Assert.False(gpu.WuaIdsIncomplete);
        Assert.False(audio.WuaIdsIncomplete);
        Assert.Equal(1, gpu.CorrelatedWuaCandidates);
        Assert.Contains("nessun candidato Windows Update correlato", audio.DisplayLine);
        Assert.DoesNotContain("ID WUA incompleti", audio.DisplayLine);
    }

    [Fact]
    public void FailedQueryIgnoresStaleUpdatesAndDoesNotReportIncompleteIds()
    {
        var row = Assert.Single(VendorDriverEvidenceFinder.Find([Gpu],
            [Offer(GuidA, Nvidia, incomplete: true)],
            wuaWasQueried: false, wuaCoveragePartial: true));
        Assert.False(row.WuaWasQueried);
        Assert.False(row.WuaCoveragePartial);
        Assert.False(row.WuaIdsIncomplete);
        Assert.Equal(0, row.CorrelatedWuaCandidates);
        Assert.Contains("ricerca Windows Update non verificata", row.DisplayLine);
        Assert.DoesNotContain("ID WUA incompleti", row.DisplayLine);
        Assert.DoesNotContain("nessun candidato Windows Update", row.DisplayLine);
    }

    [Fact]
    public void CompleteOrEmptyWuaListsPreservePreviousText()
    {
        var noMatch = Assert.Single(VendorDriverEvidenceFinder.Find([Gpu],
            [Offer(GuidA, Realtek)]));
        Assert.False(noMatch.WuaIdsIncomplete);
        Assert.Equal(0, noMatch.CorrelatedWuaCandidates);
        Assert.Contains("nessun candidato Windows Update correlato", noMatch.DisplayLine);
        Assert.DoesNotContain("ID WUA incompleti", noMatch.DisplayLine);

        var none = Assert.Single(VendorDriverEvidenceFinder.Find([Gpu], []));
        Assert.False(none.WuaIdsIncomplete);
        Assert.Contains("stato OEM non verificato", none.DisplayLine);
    }
}
