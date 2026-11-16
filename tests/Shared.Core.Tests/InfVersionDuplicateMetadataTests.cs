using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #505: duplicate [Version] scalars are ambiguous, including duplicates
/// written with different key casing or identical values. Never select a CAT
/// by taking only the first declaration.
/// </summary>
public sealed class InfVersionDuplicateMetadataTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0";

    private static DriverInfo Device() =>
        new(Gpu + @"\PRIVATE_SYNTHETIC_INSTANCE", "Generic Microsoft GPU",
            "Microsoft", "System", "Microsoft", "31.0.0.1",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "oem40.inf", true, true, 0, [Gpu], []);

    private static string Inf(string? catalogs = null,
        string? signatures = null, string? versions = null) =>
        "[Version]\n" +
        (signatures ?? "Signature=\"$Windows NT$\"") + "\n" +
        "Class=Display\n" +
        (catalogs ?? "CatalogFile.NTamd64=driver.cat") + "\n" +
        (versions ?? "DriverVer=11/01/2026,33.0.0.1") + "\n" +
        "[Manufacturer]\nVendor=Models,NTamd64\n" +
        "[Models.NTamd64]\nGpu=Install," + Gpu + "\n";

    private static InfPlatformPreflight.Report Assess(string inf) =>
        InfPlatformPreflight.Assess(inf, Device(), "x64",
            10, 0, 26100, catalogFileExists: true);

    [Theory]
    [InlineData("CatalogFile.NTamd64=driver.cat\nCatalogFile.NTamd64=other.cat")]
    [InlineData("CatalogFile.NTamd64=driver.cat\nCatalogFile.NTAMD64=driver.cat")]
    [InlineData("CatalogFile.NTamd64=\"driver.cat\"\nCatalogFile.NTamd64=other.cat")]
    public void RepeatedArchitectureCatalogCannotClaimPresentCat(string declarations)
    {
        var inf = Inf(catalogs: declarations);
        Assert.Null(InfPlatformPreflight.GetCatalogFileName(inf, "x64"));
        var report = Assess(inf);
        Assert.Equal("Non determinato", report.CatalogName);
        Assert.Contains("firma non verificata", report.CatalogStatus);
        Assert.DoesNotContain("firma valida", report.CatalogStatus);
        Assert.DoesNotContain("PRIVATE_SYNTHETIC_INSTANCE", report.Explanation);
    }

    [Fact]
    public void ConflictingArchitectureCatalogCannotFallBackToGeneric()
    {
        var inf = Inf(catalogs:
            "CatalogFile=generic.cat\n" +
            "CatalogFile.NTamd64=driver.cat\n" +
            "CatalogFile.NTamd64=other.cat");
        Assert.Null(InfPlatformPreflight.GetCatalogFileName(inf, "AMD64"));
        Assert.Equal("generic.cat",
            InfPlatformPreflight.GetCatalogFileName(inf, "ARM64"));
    }

    [Theory]
    [InlineData("CatalogFile=first.cat\nCatalogFile=second.cat")]
    [InlineData("CatalogFile.NT=first.cat\nCatalogFile.NT=second.cat\nCatalogFile=generic.cat")]
    public void RepeatedLowerPriorityCatalogCannotBecomeFallback(
        string declarations)
    {
        Assert.Null(InfPlatformPreflight.GetCatalogFileName(
            Inf(catalogs: declarations), "x64"));
    }

    [Theory]
    [InlineData("Signature=\"$Windows NT$\"\nSignature=\"$Chicago$\"")]
    [InlineData("Signature=\"$Windows NT$\"\nSIGNATURE=\"$Windows NT$\"")]
    public void RepeatedSignatureAlwaysRequiresManualReview(string declarations)
    {
        var report = Assess(Inf(signatures: declarations));
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            report.Result);
        Assert.Equal(0, report.HardwareMatchStrength);
        Assert.Contains("Signature", report.Explanation);
    }

    [Fact]
    public void DuplicateDriverVerCannotSupplyVersionEvidence()
    {
        var inf = Inf(versions:
            "DriverVer=11/01/2026,33.0.0.1\n" +
            "DriverVer=11/02/2026,99.0.0.1");
        var report = Assess(inf);
        Assert.Equal("Non dichiarata", report.DriverVersion);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            report.Result);
        Assert.Equal(2, report.HardwareMatchStrength);

        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("gpu.inf").Open(), new UTF8Encoding(false));
            writer.Write(inf);
        }
        data.Position = 0;
        var comparison = LocalOemDriverComparison.Compare(
            data, [Device()], "x64", 10, 0, 26100);
        var finding = Assert.Single(comparison.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            finding.VersionComparison);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(0, comparison.PotentialNewerDevices);
        Assert.False(comparison.IsInstallable);
        Assert.False(comparison.IsVerifiedCompatible);
    }

    [Fact]
    public void FolderCatPlannerNeverChecksAnAmbiguousCatalog()
    {
        var root = Directory.CreateTempSubdirectory("Omega-VersionDupe-505-")
            .FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "gpu.inf"), Inf(catalogs:
                "CatalogFile.NTamd64=driver.cat\n" +
                "CatalogFile.NTamd64=other.cat"));
            File.WriteAllText(Path.Combine(root, "driver.cat"),
                "SYNTHETIC UNSIGNED CATALOG");
            var plan = OemFolderCatalogAuditPlan.Create(
                root, [Device()], "x64");
            Assert.Equal(1, plan.MatchedInf);
            Assert.Equal(1, plan.MissingCatalog);
            Assert.Empty(plan.Checks);
            Assert.False(plan.IsFullPackageVerified);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DistinctVersionKeysAndArchitecturePrecedenceStillWork()
    {
        var inf = Inf(catalogs:
            "CatalogFile=generic.cat\n" +
            "CatalogFile.NT=nt.cat\n" +
            "CatalogFile.NTamd64=driver.cat");
        Assert.Equal("driver.cat",
            InfPlatformPreflight.GetCatalogFileName(inf, "x64"));
        Assert.Equal("nt.cat",
            InfPlatformPreflight.GetCatalogFileName(inf, "ARM64"));
        var report = Assess(inf);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            report.Result);
        Assert.Equal("driver.cat", report.CatalogName);
        Assert.Equal(2, report.HardwareMatchStrength);
        Assert.Contains("NON verificata", report.CatalogStatus);
    }
}
