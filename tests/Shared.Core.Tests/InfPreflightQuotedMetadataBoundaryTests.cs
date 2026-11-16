using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #504: preflight version scalars and TargetOS Manufacturer declarations
/// use the SAME bounded, strictly quoted INF field parser as PnP discovery.
/// An escaped quote must not turn a fake CAT declaration into a local file.
/// </summary>
public sealed class InfPreflightQuotedMetadataBoundaryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string id) =>
        new(id + @"\SYNTHETIC_PRIVATE_INSTANCE", "Generic Microsoft device",
            "Microsoft", "System", "Microsoft", "31.0.0.1",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "oem37.inf", true, true, 0, [id], []);

    private static string Inf(string catalog = "nvidia.cat",
        string signature = "\"$Windows NT$\"",
        string manufacturer = "Vendor=Models,NTamd64.10.0...22000",
        string models = "", string className = "Display") =>
        "[Version]\nSignature=" + signature +
        "\nClass=" + className +
        "\nCatalogFile.NTamd64=" + catalog +
        "\nDriverVer=11/10/2026,33.0.0.1" +
        "\n[Manufacturer]\n" + manufacturer +
        "\n[Models.NTamd64.10.0...22000]\n" +
        (models.Length > 0 ? models : "Gpu=Install," + Nvidia) + "\n";

    private static InfPlatformPreflight.Report Evaluate(string inf,
        string pnp = Nvidia, int build = 22631) =>
        InfPlatformPreflight.Assess(inf, Device(pnp), "x64",
            10, 0, build, catalogFileExists: true);

    [Theory]
    [InlineData("\"\"nvidia.cat\"\"")]
    [InlineData("\"nvidia.cat\"\"\"")]
    [InlineData("\"nvidia.cat\"junk")]
    [InlineData("nvidia.cat\"\"")]
    [InlineData("\"nvidia.cat,backup.cat\"")]
    [InlineData("\"\"\"\"nvidia.cat\"\"\"\"")]
    public void MalformedOrEscapedCatalogScalarNeverCreatesValidCatMatch(
        string catalog)
    {
        var inf = Inf(catalog: catalog);
        Assert.Null(InfPlatformPreflight.GetCatalogFileName(inf, "x64"));
        var report = Evaluate(inf);
        Assert.Equal("Non determinato", report.CatalogName);
        Assert.Contains("firma non verificata", report.CatalogStatus);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            report.Result);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_INSTANCE", report.Explanation);
    }

    [Fact]
    public void MalformedArchitectureSpecificCatalogCannotFallbackToGeneric()
    {
        var inf = Inf(catalog: "\"\"nvidia.cat\"\"").Replace(
            "CatalogFile.NTamd64=", "CatalogFile=generic.cat\nCatalogFile.NTamd64=");
        Assert.Null(InfPlatformPreflight.GetCatalogFileName(inf, "x64"));
        Assert.Equal("generic.cat",
            InfPlatformPreflight.GetCatalogFileName(inf, "arm64"));
        Assert.DoesNotContain("firma valida", Evaluate(inf).CatalogStatus);
    }

    [Theory]
    [InlineData("\"nvidia.cat\"")]
    [InlineData("nvidia.cat")]
    public void OrdinaryScalarCatalogRemainsReadOnlyEvidence(string catalog)
    {
        var inf = Inf(catalog: catalog);
        Assert.Equal("nvidia.cat",
            InfPlatformPreflight.GetCatalogFileName(inf, "x64"));
        var report = Evaluate(inf);
        Assert.Equal("nvidia.cat", report.CatalogName);
        Assert.Contains("firma digitale NON verificata", report.CatalogStatus);
        Assert.Equal(2, report.HardwareMatchStrength);
    }

    [Theory]
    [InlineData("\"\"$Windows NT$\"\"")]
    [InlineData("\"$Windows NT$\"\"\"")]
    [InlineData("\"$Windows NT$\"junk")]
    public void MalformedSignatureCannotPassVersionPreflight(string signature)
    {
        var outcome = Evaluate(Inf(signature: signature));
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            outcome.Result);
        Assert.Equal(0, outcome.HardwareMatchStrength);
        Assert.Contains("Signature", outcome.Explanation);
        Assert.False(outcome.CatalogStatus.Contains("firma valida"));
    }

    [Theory]
    [InlineData("Vendor=Models,NTamd64.10.0...22000,\"NTamd64.10.0...26100\"junk")]
    [InlineData("Vendor=\"Models\"junk,NTamd64.10.0...22000")]
    [InlineData("Vendor=Models,NTamd64.10.0...22000,NTamd64\"extra\"")]
    public void TargetOsPreflightRejectsWholeMalformedManufacturerRecord(
        string manufacturer)
    {
        var inf = Inf(manufacturer: manufacturer);
        Assert.Throws<InvalidDataException>(() => Evaluate(inf));
        Assert.Throws<InvalidDataException>(() =>
            InfModelEvidence.ReadMatchingIds(inf, [Device(Nvidia)]));
    }

    [Fact]
    public void QuotedManufacturerTargetOsStillSelectsMostSpecificModels()
    {
        var text = Inf(manufacturer:
            "Vendor=\"Models\",\"NTamd64.10.0...22000\",\"NTamd64.10.0...26100\"");
        text += "[Models.NTamd64.10.0...26100]\n" +
            "Wifi=Install," + Killer + "\n";
        var oldWindows = Evaluate(text);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            oldWindows.Result);
        Assert.Equal(2, oldWindows.HardwareMatchStrength);

        var newWindows = Evaluate(text, build: 26100);
        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            newWindows.Result);
        Assert.Equal(0, newWindows.HardwareMatchStrength);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_INSTANCE", newWindows.Explanation);
    }

    [Fact]
    public void IndependentQuotedManufacturerRowCanStillSelectOlderDriver()
    {
        var text = Inf(manufacturer:
            "Vendor=Models,NTamd64.10.0...22000,NTamd64.10.0...26100\n" +
            "Vendor2=\"Models\",\"NTamd64.10.0...22000\"");
        text += "[Models.NTamd64.10.0...26100]\n" +
            "Audio=Install," + Realtek + "\n";
        var report = Evaluate(text, build: 26100);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            report.Result);
        Assert.Equal(2, report.HardwareMatchStrength);
    }

    [Fact]
    public void CatalogFromQuotedInfPersistsOnlyAsUnverifiedZipEvidence()
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (var writer = new StreamWriter(
                zip.CreateEntry("oem/nvidia.inf").Open(), new UTF8Encoding(false)))
                writer.Write(Inf(catalog: "\"\"nvidia.cat\"\""));
            using (var cat = new StreamWriter(
                zip.CreateEntry("oem/nvidia.cat").Open(), new UTF8Encoding(false)))
                cat.Write("SYNTHETIC UNSIGNED CATALOG");
        }
        data.Position = 0;
        var report = LocalOemDriverComparison.Compare(
            data, [Device(Nvidia)], "x64", 10, 0, 22631);
        var finding = Assert.Single(report.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            finding.PlatformVerdict);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_INSTANCE", report.Summary);
    }

    [Fact]
    public void QuotedNormalCatalogWorksWithPhysicalReadOnlyAuditPlanner()
    {
        var root = Directory.CreateTempSubdirectory("Omega-CatalogQuote-504-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "display.inf"),
                Inf(catalog: "\"nvidia.cat\""));
            File.WriteAllText(Path.Combine(root, "nvidia.cat"),
                "SYNTHETIC UNSIGNED CATALOG");
            var plan = OemFolderCatalogAuditPlan.Create(
                root, [Device(Nvidia)], "x64");
            Assert.Equal(1, plan.MatchedInf);
            Assert.Single(plan.Checks);
            Assert.False(plan.IsFullPackageVerified);

            File.WriteAllText(Path.Combine(root, "display.inf"),
                Inf(catalog: "\"\"nvidia.cat\"\""));
            var invalid = OemFolderCatalogAuditPlan.Create(
                root, [Device(Nvidia)], "x64");
            Assert.Equal(1, invalid.MatchedInf);
            Assert.Equal(1, invalid.MissingCatalog);
            Assert.Empty(invalid.Checks);
            Assert.False(invalid.IsFullPackageVerified);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
