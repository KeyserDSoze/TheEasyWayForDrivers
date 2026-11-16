using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #494: the first redacted private OEM title must not preempt a later
/// readable driver name with the SAME verified package ID.
/// </summary>
public sealed class OemRedactedDuplicateRecoveryTests
{
    private static string Hp(params object[] rows) =>
        JsonSerializer.Serialize(rows);

    private static string Lenovo(params object[] rows) =>
        JsonSerializer.Serialize(rows);

    private static object HpRow(string id, string name, string category,
        string version = "1.0") => new
        {
            Id = id, Name = name, Category = category,
            Version = version, ReleaseType = "Recommended",
            ReleaseDate = "2026-11-16"
        };

    private static object LenovoRow(string id, string name,
        string category = "Graphics", string type = "Driver",
        bool applicable = true, bool installed = false) => new
        {
            PackageID = id, Title = name, Type = type, Category = category,
            IsApplicable = applicable, IsInstalled = installed,
            Version = "32.0", ReleaseDate = "2026-11-16",
            Severity = "Recommended"
        };

    [Fact]
    public void HpRedactedFirstEntryCannotHideLaterSafeGraphicsSubcategory()
    {
        var first = HpRow("SP500", "owner@example.invalid",
            "Driver - Audio", "0.1");
        var safe = HpRow("sp500", "NVIDIA RTX 4060 Graphics",
            "Driver - Graphics", "32.0");
        var rows = HpSoftpaqCatalogParser.Parse(Hp(first, safe,
            HpRow("sp600", "Killer Wi-Fi 6E", "Driver - Network")));
        Assert.Equal(2, rows.Count);
        var nvidia = Assert.Single(rows, row =>
            row.Id.Equals("SP500", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("NVIDIA RTX 4060 Graphics", nvidia.Title);
        Assert.Equal("32.0", nvidia.Version);
        Assert.All(DiscoveryResultAdapter.FromHp(rows),
            row => Assert.Equal("Solo catalogo · non installabile",
                row.VerificationLabel));
        Assert.DoesNotContain("owner@example.invalid",
            string.Join(" ", rows.Select(r => r.Title)));
    }

    [Fact]
    public void HpCategoryFairSelectionUsesRecoveredCategoryAndKeepsRareGpu()
    {
        var first = HpRow("sp-gpu", @"C:\Users\PRIVATE_OEM\gpu.inf",
            "Driver - Audio");
        var audio = Enumerable.Range(0, 140)
            .Select(i => HpRow($"sp-audio{i}", "Realtek Audio",
                "Driver - Audio"));
        var last = HpRow("sp-gpu", "NVIDIA GeForce RTX 4060",
            "Driver - Graphics", "31.0");
        var rows = HpSoftpaqCatalogParser.Parse(
            Hp([first, .. audio, last]));
        Assert.Equal(HpSoftpaqCatalogParser.MaximumDriverResults,
            rows.Count);
        Assert.Contains(rows, row => row.Id == "sp-gpu" &&
            row.Title == "NVIDIA GeForce RTX 4060" &&
            row.Version == "31.0");
        Assert.DoesNotContain(rows, row =>
            row.Title.Contains("PRIVATE_OEM", StringComparison.Ordinal));
    }

    [Fact]
    public void HpFirstSafeRemainsWinnerAndSinglePrivateTitleStaysRedacted()
    {
        var rows = HpSoftpaqCatalogParser.Parse(Hp(
            HpRow("sp1", "Intel Killer Wi-Fi", "Driver - Network", "24.2"),
            HpRow("SP1", "PRIVATE_SECRET_KILLER",
                "Driver - Graphics", "99.9"),
            HpRow("sp2", "PRIVATE_CUSTOMER_DEVICE",
                "Driver - Graphics", "8.0")));
        Assert.Equal(2, rows.Count);
        Assert.Equal("Intel Killer Wi-Fi",
            Assert.Single(rows, r => r.Id == "sp1").Title);
        Assert.Equal("24.2", Assert.Single(rows, r => r.Id == "sp1").Version);
        Assert.Equal("[dato riservato]",
            Assert.Single(rows, r => r.Id == "sp2").Title);
    }

    [Fact]
    public void LenovoApplicableDriverPrefersLaterPublicTitleAndRetainsGates()
    {
        var rows = LenovoApplicableUpdateParser.Parse(Lenovo(
            LenovoRow("LN-55", "owner@example.invalid", "Audio"),
            LenovoRow("LN-55", "BIOS Driver", type: "BIOS"),
            LenovoRow("LN-55", "Already installed", installed: true),
            LenovoRow("ln-55", "NVIDIA Graphics", "Graphics"),
            LenovoRow("LN-56", "Intel Killer Wi-Fi", "Network")));
        Assert.Equal(2, rows.Count);
        var nvidia = Assert.Single(rows, r =>
            r.PackageId.Equals("LN-55", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("NVIDIA Graphics", nvidia.Title);
        Assert.Equal("32.0", nvidia.Version);
        Assert.All(LenovoApplicableUpdateParser.ToVendorChecks(rows),
            item => Assert.False(item.IsInstallable));
        Assert.DoesNotContain("owner@example.invalid",
            string.Join(" ", rows.Select(r => r.Title)));
    }

    [Fact]
    public void LenovoCategoryFairnessKeepsRecoveredNvidiaAfterAudioFlood()
    {
        var first = LenovoRow("LN-GPU", "PRIVATE_DEVICE_123", "Audio");
        var audio = Enumerable.Range(0, 110)
            .Select(i => LenovoRow($"LN-AUD{i}", "Audio", "Audio"));
        var last = LenovoRow("LN-GPU", "NVIDIA RTX 4060", "Graphics");
        var parsed = LenovoApplicableUpdateParser.Parse(
            Lenovo([first, .. audio, last]));
        Assert.Equal(LenovoApplicableUpdateParser.MaximumDriverResults,
            parsed.Count);
        Assert.Contains(parsed, p => p.PackageId == "LN-GPU" &&
            p.Title == "NVIDIA RTX 4060");
        Assert.All(parsed, p =>
            Assert.DoesNotContain("PRIVATE_", p.Title));
    }

    [Fact]
    public void LenovoSafeFirstWinsAndLonePrivateRowRemainsRedacted()
    {
        var found = LenovoApplicableUpdateParser.Parse(Lenovo(
            LenovoRow("ln1", "Realtek Audio"),
            LenovoRow("LN1", "PRIVATE_OWNER_NAME"),
            LenovoRow("ln2", "PRIVATE_CUSTOMER_PATH")));
        Assert.Equal(2, found.Count);
        Assert.Equal("Realtek Audio",
            Assert.Single(found, p => p.PackageId == "ln1").Title);
        Assert.Equal("[dato riservato]",
            Assert.Single(found, p => p.PackageId == "ln2").Title);
    }

    [Fact]
    public void DellDcuPrivateFirstDriverDoesNotHideLegitimateSameId()
    {
        const string xml = """
            <Updates>
              <Update id="GPU42" name="owner@example.invalid" type="Driver"
                      version="0.1" severity="Critical" />
              <Update id="GPU42" name="Other report" type="Application" />
              <Update id="gpu42" name="NVIDIA RTX 4060" type="Driver"
                      version="32.0" severity="Recommended" />
              <Update id="wifi77" name="Intel Killer Wi-Fi" type="Driver"
                      version="24.8" />
            </Updates>
            """;
        var result = DellApplicableUpdateParser.Parse(xml);
        Assert.Equal(2, result.Count);
        Assert.Equal("NVIDIA RTX 4060", result[0].Title);
        Assert.Equal("32.0", result[0].Version);
        Assert.Equal("Recommended", result[0].Severity);
        Assert.Equal("Intel Killer Wi-Fi", result[1].Title);
        Assert.All(DellApplicableUpdateParser.ToVendorChecks(result),
            row => Assert.False(row.IsInstallable));
        Assert.DoesNotContain("owner@example.invalid",
            string.Join(" ", result.Select(r => r.Title)));
    }

    [Fact]
    public void DellSafeFirstWinsEvenAtExactDriverLimit()
    {
        var extras = string.Concat(Enumerable.Range(1, 149)
            .Select(i => $"""<Update id="d{i}" name="Realtek" type="Driver"/>"""));
        var xml = "<Updates>" +
            """<Update id="gpu" name="NVIDIA" type="Driver" version="32.0"/>""" +
            extras +
            """<Update id="gpu" name="PRIVATE_CUSTOMER_ORDER" type="Driver"/>""" +
            "</Updates>";
        var rows = DellApplicableUpdateParser.Parse(xml);
        Assert.Equal(150, rows.Count);
        Assert.Equal("NVIDIA", rows[0].Title);
        Assert.Equal("32.0", rows[0].Version);
    }

    [Fact]
    public void DellPrivateTitleAloneStillRedactedAndFailClosedTypeGate()
    {
        const string xml = """
            <Updates>
              <Update id="gpu" name="PRIVATE_CUSTOMER_SERIAL"
                      type="Driver" version="0.2"/>
            </Updates>
            """;
        var item = Assert.Single(DellApplicableUpdateParser.Parse(xml));
        Assert.Equal("[dato riservato]", item.Title);
        Assert.Equal("gpu", item.Id);
        Assert.Throws<InvalidDataException>(() =>
            DellApplicableUpdateParser.Parse(
                """<Updates><Update id="gpu" name="GPU"/></Updates>"""));
    }
}
