using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #534: the vendor Home matrix and anonymous QA copy must disclose
/// incomplete WUA Type=Driver PnP ID lists without inventing updates.
/// </summary>
public sealed class VendorInventoryIncompleteWuaIdTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Different = @"PCI\VEN_9999&DEV_0001";
    private const string WuaGuid = "00000000-0000-4000-8000-000000000534";

    private static DriverInfo Device(string name, string hardware,
        string deviceClass) =>
        new("PRIVATE_" + name + "_SERIAL", name, "Microsoft", deviceClass,
            "Microsoft", "30.0", null, "oem12.inf", true, true, 0,
            [hardware], []);

    private static DriverUpdateInfo Update(string hardware,
        string source = "Windows Update online", bool incomplete = false) =>
        new(WuaGuid, "PRIVATE_DRIVER_TITLE", null, "Display", "Microsoft",
            null, null, false, "Microsoft", null, hardware, null,
            SearchSource: source, AdditionalHardwareIdsIncomplete: incomplete);

    private static readonly DriverInfo Gpu = Device(
        "Microsoft Basic Display Adapter", Nvidia, "Display");
    private static readonly DriverInfo Audio = Device(
        "High Definition Audio Device", Realtek, "MEDIA");

    private static DriverSearchReport Report(
        IReadOnlyList<DriverUpdateInfo> items, bool partial = false) =>
        new(items, partial
            ? [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_WSUS_URL"),
               new DriverSearchPassStatus("Microsoft Update online",
                "NetworkError", 0, "PRIVATE_PROXY")]
            : [new DriverSearchPassStatus("Windows Update online",
                "Completed", 1, "PRIVATE_WSUS_URL")]);

    [Fact]
    public void UnmatchedVendorDoesNotClaimMeasuredAbsenceWhenWuaIdsTruncated()
    {
        var updates = new[] { Update(Different, incomplete: true) };
        var matrix = VendorInventoryCoverage.Create([Gpu, Audio], updates);
        Assert.True(matrix.CorrelationsComplete);
        Assert.True(matrix.WuaIdsIncomplete);
        Assert.Contains("ID PnP incompleti", matrix.ScopeNote);
        Assert.All(matrix.Rows, row =>
        {
            Assert.True(row.WuaIdsIncomplete);
            Assert.Equal(0, row.DevicesWithWuaMatches);
            Assert.Contains("ID WUA incompleti", row.DisplayLine(true));
            Assert.Contains("assenza non verificabile", row.DisplayLine(true));
            Assert.DoesNotContain("0 con candidati Windows Update",
                row.DisplayLine(true));
        });

        var text = ScanCoverageText.Create(Report(updates), matrix.Rows, 2, 0,
            0, [Gpu, Audio], inventoryComplete: true, vendorCoverage: matrix);
        Assert.Contains("ID WUA incompleti: zero match osservati", text);
        Assert.DoesNotContain("0 con corrispondenze WUA per ID", text);
        Assert.DoesNotContain("PRIVATE_", text);
        Assert.DoesNotContain(WuaGuid, text);
        Assert.DoesNotContain(Different, text);
        Assert.DoesNotContain(Nvidia, text);
    }

    [Fact]
    public void ExactKnownGpuAssociationRemainsVisibleWithIncompleteIds()
    {
        var updates = new[] { Update(Nvidia, incomplete: true) };
        var matrix = VendorInventoryCoverage.Create([Gpu, Audio], updates);
        var gpu = Assert.Single(matrix.Rows, r => r.Vendor == "NVIDIA");
        var audio = Assert.Single(matrix.Rows, r => r.Vendor == "Realtek");

        Assert.Equal(1, gpu.DevicesWithWuaMatches);
        Assert.Equal(1, gpu.WuaCorrelations);
        Assert.Contains("ulteriori associazioni possibili", gpu.DisplayLine(true));
        Assert.Equal(0, audio.DevicesWithWuaMatches);
        Assert.Contains("assenza non verificabile", audio.DisplayLine(true));
        Assert.True(matrix.WuaIdsIncomplete);

        var copy = ScanCoverageText.Create(Report(updates), matrix.Rows,
            2, 0, 0, [Gpu, Audio], true, vendorCoverage: matrix);
        Assert.Contains("almeno", copy); // other diagnostics may also use it
        Assert.Contains("ID WUA incompleti: ulteriori associazioni possibili",
            copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void PartialSourcesAndIncompleteDeviceIdsAreBothExplained()
    {
        var updates = new[] { Update(Different, incomplete: true) };
        var matrix = VendorInventoryCoverage.Create([Gpu], updates,
            wuaSourcesPartial: true);
        var row = Assert.Single(matrix.Rows);
        var line = row.DisplayLine(true);
        Assert.Contains("ricerca PARZIALE", line);
        Assert.Contains("ID WUA incompleti", line);
        Assert.Contains("assenza non verificabile", line);
        Assert.Contains("Ricerca WUA Type=Driver PARZIALE", matrix.ScopeNote);
        Assert.Contains("ID PnP incompleti", matrix.ScopeNote);

        var copy = ScanCoverageText.Create(Report(updates, true), matrix.Rows,
            1, 0, 0, [Gpu], true, vendorCoverage: matrix);
        Assert.Contains("ricerca PARZIALE", copy);
        Assert.Contains("ID WUA incompleti", copy);
        Assert.DoesNotContain("PRIVATE_", copy);
    }

    [Fact]
    public void UnknownSourceIncompleteFlagDoesNotPolluteTrustedMatrix()
    {
        var unknown = Update(Different, "PRIVATE_OEM_MIRROR",
            incomplete: true);
        var trusted = Update(Nvidia) with
        {
            Id = "00000000-0000-4000-8000-000000000535"
        };
        var matrix = VendorInventoryCoverage.Create([Gpu, Audio],
            [unknown, trusted]);
        Assert.False(matrix.WuaIdsIncomplete);
        Assert.DoesNotContain("ID PnP incompleti", matrix.ScopeNote);
        Assert.All(matrix.Rows, row =>
            Assert.DoesNotContain("ID WUA incompleti", row.DisplayLine(true)));
        Assert.Contains("0 con candidati Windows Update",
            Assert.Single(matrix.Rows, row => row.Vendor == "Realtek")
                .DisplayLine(true));
        Assert.Equal(1, Assert.Single(matrix.Rows,
            row => row.Vendor == "NVIDIA").DevicesWithWuaMatches);
    }

    [Fact]
    public void SameGuidFromTwoWuaSourcesPreservesIncompleteAssociation()
    {
        var first = Update(Nvidia, "Windows configurato");
        var second = Update(Realtek, "Windows Update online",
            incomplete: true);
        var matrix = VendorInventoryCoverage.Create([Gpu, Audio],
            [first, second]);
        Assert.True(matrix.WuaIdsIncomplete);
        Assert.Equal(2, matrix.Rows.Sum(r => r.DevicesWithWuaMatches));
        Assert.All(matrix.Rows, row =>
        {
            Assert.Equal(1, row.WuaCorrelations);
            Assert.Contains("ulteriori associazioni possibili",
                row.DisplayLine(true));
        });
    }

    [Fact]
    public void CompleteIdsAndBudgetOverflowRetainTheirExistingBehavior()
    {
        var complete = VendorInventoryCoverage.Create([Gpu, Audio],
            [Update(Different)]);
        Assert.False(complete.WuaIdsIncomplete);
        Assert.Contains("0 con candidati Windows Update",
            Assert.Single(complete.Rows, r => r.Vendor == "NVIDIA")
                .DisplayLine(true));

        var oversized = Enumerable.Range(0,
            VendorInventoryCoverage.MaximumDriverUpdates + 1)
            .Select(i => Update(Different, incomplete: true) with
            {
                Id = $"00000000-0000-4000-8000-{i + 1:D12}"
            }).ToArray();
        var unsafeToCorrelate = VendorInventoryCoverage.Create([Gpu],
            oversized);
        Assert.False(unsafeToCorrelate.CorrelationsComplete);
        Assert.Contains("correlazioni WUA non valutabili",
            Assert.Single(unsafeToCorrelate.Rows).DisplayLine(true));
        Assert.DoesNotContain("0 con candidati Windows Update",
            Assert.Single(unsafeToCorrelate.Rows).DisplayLine(true));
    }
}
