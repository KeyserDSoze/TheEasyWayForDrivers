using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #507: Class/ClassGuid [Version] scalars must be quote-safe. Unparseable
/// metadata is review-only, never invented Firmware or an ordinary update.
/// </summary>
public sealed class InfFirmwareClassQuotedEvidenceTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string FirmwareGuid =
        "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}";

    private static DriverInfo Device(string id = Nvidia) =>
        new(id + @"\PRIVATE_SYNTHETIC_INSTANCE", "Generic Windows device",
            "Microsoft", "System", "Microsoft", "31.0.0.1",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "oem50.inf", true, true, 0, [id], []);

    private static string Inf(string classFields, string id = Nvidia,
        string version = "33.0.0.1") => $"""
        [Version]
        Signature="$Windows NT$"
        {classFields}
        CatalogFile.NTamd64=driver.cat
        DriverVer=11/16/2026,{version}
        [Manufacturer]
        Vendor=Models,NTamd64.10.0
        [Models.NTamd64.10.0]
        Device=Install,{id}
        """;

    private static LocalOemDriverComparison.Report Compare(
        params (string Name, string Data)[] files)
    {
        using var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(),
                    new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        data.Position = 0;
        return LocalOemDriverComparison.Compare(
            data, [Device()], "x64", 10, 0, 26100);
    }

    [Theory]
    [InlineData("Class=\"\"\"Firmware\"\"\"")]
    [InlineData("Class=\"\"Firmware\"\"")]
    [InlineData("Class=\"Firmware\"junk")]
    [InlineData("Class=Firmware\"junk\"")]
    [InlineData("ClassGuid=\"\"\"{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}\"\"\"")]
    [InlineData("ClassGuid=\"{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}\"junk")]
    [InlineData("Class=Display\nClass=Net")]
    [InlineData("Class=Display\nCLASS=Display")]
    [InlineData("ClassGuid={4d36e968-e325-11ce-bfc1-08002be10318}\nCLASSGUID={4d36e968-e325-11ce-bfc1-08002be10318}")]
    [InlineData("Class=Display\nClassGuid=not-a-guid")]
    public void MalformedOrDuplicatedClassIsUnresolvedNotFirmware(
        string classFields)
    {
        var inf = Inf(classFields);
        Assert.Equal(InfPlatformPreflight.FirmwareClassEvidence.Ambiguous,
            InfPlatformPreflight.GetFirmwareClassEvidence(inf));
        Assert.False(InfPlatformPreflight.IsFirmwareUpdateClass(inf));
    }

    [Theory]
    [InlineData("Class=Firmware")]
    [InlineData("Class=\"Firmware\"")]
    [InlineData("Class=BIOS")]
    [InlineData("Class=\"BIOS\"")]
    [InlineData("ClassGuid={f2e7dd72-6468-4e36-b6f1-6488f42c1b52}")]
    [InlineData("ClassGuid=\"{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}\"")]
    [InlineData("Class=Display\nClassGuid={f2e7dd72-6468-4e36-b6f1-6488f42c1b52}")]
    [InlineData("Class=Display\nClass=Firmware")]
    [InlineData("Class=Firmware\nClass=Display")]
    public void ValidFirmwareMetadataAlwaysKeepsTheFirmwareVeto(
        string classFields)
    {
        var inf = Inf(classFields);
        Assert.Equal(InfPlatformPreflight.FirmwareClassEvidence.Firmware,
            InfPlatformPreflight.GetFirmwareClassEvidence(inf));
        Assert.True(InfPlatformPreflight.IsFirmwareUpdateClass(inf));
    }

    [Theory]
    [InlineData("Class=Display")]
    [InlineData("Class=Net")]
    [InlineData("Class=MEDIA\nClassGuid={4d36e96c-e325-11ce-bfc1-08002be10318}")]
    public void OrdinaryValidClassHasNoFirmwareEvidence(string classFields)
    {
        Assert.Equal(InfPlatformPreflight.FirmwareClassEvidence.NoFirmwareEvidence,
            InfPlatformPreflight.GetFirmwareClassEvidence(Inf(classFields)));
    }

    [Theory]
    [InlineData("Class=\"\"\"Firmware\"\"\"")]
    [InlineData("Class=Display\nCLASS=Net")]
    [InlineData("Class=Display\nClassGuid=not-a-guid")]
    public void AmbiguousZipInfKeepsPnpButNoNewerDriverClaim(
        string classFields)
    {
        var report = Compare(("gpu.inf", Inf(classFields)));
        var finding = Assert.Single(report.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.True(finding.IsSetupClassAmbiguous);
        Assert.False(finding.IsFirmwareInf);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            finding.PlatformVerdict);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            finding.VersionComparison);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.NotVerified,
            finding.DriverVerDateRelation);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Contains("Class/ClassGuid INF non interpretabili", finding.Summary);
        Assert.DoesNotContain("PRIVATE_SYNTHETIC_INSTANCE", finding.Summary);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void ValidOrdinaryDriverOutranksMalformedFirmwareNamesakeInZip()
    {
        var report = Compare(
            ("ambiguous.inf", Inf("Class=\"\"\"Firmware\"\"\"", version: "99.0.0.1")),
            ("display.inf", Inf("Class=Display")));
        var finding = Assert.Single(report.Findings);
        Assert.False(finding.IsSetupClassAmbiguous);
        Assert.False(finding.IsFirmwareInf);
        Assert.Equal("display.inf", finding.InfName);
        Assert.Equal("33.0.0.1", finding.PackageVersion);
        Assert.True(finding.IsPotentialNewer);
        Assert.Equal(1, report.AdditionalMatchesOmitted);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void FolderCatPlannerExcludesAmbiguousClassButKeepsValidDriver()
    {
        var folder = Directory.CreateTempSubdirectory(
            "Omega-FirmwareClass-507-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "driver.cat"),
                "SYNTHETIC UNSIGNED CATALOG");
            File.WriteAllText(Path.Combine(folder, "bad.inf"),
                Inf("Class=\"\"\"Firmware\"\"\""));
            File.WriteAllText(Path.Combine(folder, "good.inf"),
                Inf("Class=Net", Killer));

            var plan = OemFolderCatalogAuditPlan.Create(
                folder, [Device(), Device(Killer)], "x64");

            Assert.Equal(2, plan.InfExamined);
            Assert.Equal(1, plan.UnreadableInf);
            Assert.Equal(1, plan.MatchedInf);
            var check = Assert.Single(plan.Checks);
            Assert.Equal("good.inf", Path.GetFileName(check.InfPath));
            Assert.Equal(0, plan.FirmwareInfChecks);
            Assert.False(plan.IsFullPackageVerified);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
