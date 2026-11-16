using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #495: a numerically larger OEM INF DriverVer cannot be treated as
/// potentially newer if its actual INF date predates the installed driver.
/// </summary>
public sealed class LocalOemDriverDateEvidenceTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string id, string version,
        DateTimeOffset? date) =>
        new(id + @"\INSTANCE_PRIVATE", "Generic driver", "Microsoft",
            "System", "Microsoft", version, date, "oem10.inf",
            true, true, 0, [id], []);

    private static string Inf(string id, string driverDate,
        string version = "40.0.0.1") => $"""
        [Version]
        Signature="$Windows NT$"
        Class=System
        CatalogFile.NTamd64=test.cat
        DriverVer={driverDate},{version}
        [Manufacturer]
        %Maker%=Models,NTamd64.10.0
        [Models.NTamd64.10.0]
        %Device%=Install,{id}
        """;

    private static MemoryStream Zip(
        params (string Path, string InfText)[] files)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var file in files)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(file.Path).Open(),
                    new UTF8Encoding(false));
                writer.Write(file.InfText);
            }
        }
        output.Position = 0;
        return output;
    }

    private static DateTimeOffset Installed(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void OlderInfDateVetoesNumericallyHigherNvidiaVersionHint()
    {
        using var archive = Zip(("nvidia.inf", Inf(Nvidia, "1/15/2022")));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia, "32.0.0.1", Installed(2026, 11, 1))],
            "x64", 10, 0, 26100);
        var finding = Assert.Single(report.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            finding.VersionComparison);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.Older,
            finding.DriverVerDateRelation);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Contains("data INF precedente", finding.Summary);
        Assert.Contains("solo revisione", finding.Summary);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("INSTANCE_PRIVATE", report.Summary);
    }

    [Theory]
    [InlineData("11/1/2026", "Equal")]
    [InlineData("11/2/2026", "Newer")]
    public void EqualOrNewerInfDateKeepsVersionHintReadOnly(
        string infDate, string relation)
    {
        using var archive = Zip(("killer.inf", Inf(Killer, infDate,
            "25.0.0.1")));
        var result = LocalOemDriverComparison.Compare(archive,
            [Device(Killer, "24.0.0.1", Installed(2026, 11, 1))],
            "x64", 10, 0, 26100);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(relation, finding.DriverVerDateRelation.ToString());
        Assert.True(finding.IsPotentialNewer);
        Assert.Equal(1, result.PotentialNewerDevices);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void MissingInstalledDriverDateRemainsInconclusive()
    {
        using var archive = Zip(("audio.inf", Inf(Realtek, "10/4/2026")));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device(Realtek, "9.0.0.0", null)], "x64", 10, 0, 26100);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.NotVerified,
            finding.DriverVerDateRelation);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            finding.VersionComparison);
        Assert.True(finding.IsPotentialNewer);
        Assert.Contains("confronto date INF/driver non verificabile",
            finding.Summary);
        Assert.False(report.IsInstallable);
    }

    [Theory]
    [InlineData("02/29/2025")]
    [InlineData("13/12/2026")]
    [InlineData("0/10/2026")]
    [InlineData("11/31/2026")]
    public void ImpossibleInfDateNeverCreatesNewVersionOffer(string infDate)
    {
        using var archive = Zip(("driver.inf", Inf(Nvidia, infDate)));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia, "32.0.0.1", Installed(2026, 1, 1))],
            "x64", 10, 0, 26100);
        var row = Assert.Single(report.Findings);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.NotVerified,
            row.DriverVerDateRelation);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void LeapDayAndOffsetDatePreserveInfCalendarDay()
    {
        using var archive = Zip(("driver.inf", Inf(Nvidia, "02/29/2024")));
        var installed = new DateTimeOffset(2024, 2, 29, 0, 0, 0,
            TimeSpan.FromHours(3));
        var result = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia, "32.0.0.1", installed)],
            "x64", 10, 0, 26100);
        var row = Assert.Single(result.Findings);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.Equal,
            row.DriverVerDateRelation);
        Assert.True(row.IsPotentialNewer);
    }

    [Fact]
    public void SameDeviceSelectsNewerDatedInfRatherThanOlderHigherVersion()
    {
        using var archive = Zip(
            ("1-old/graphics.inf", Inf(Nvidia, "1/15/2022", "99.0.0.1")),
            ("2-current/graphics.inf", Inf(Nvidia, "11/5/2026", "33.0.0.1")));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia, "32.0.0.1", Installed(2026, 1, 1))],
            "x64", 10, 0, 26100);
        var chosen = Assert.Single(report.Findings);
        Assert.Equal("33.0.0.1", chosen.PackageVersion);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.Newer,
            chosen.DriverVerDateRelation);
        Assert.True(chosen.IsPotentialNewer);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.Equal(1, report.AdditionalMatchesOmitted);
    }

    [Fact]
    public void MultiVendorPreviewKeepsNvidiaKillerAndRealtekReadOnly()
    {
        using var archive = Zip(
            ("nvidia.inf", Inf(Nvidia, "01/01/2023")),
            ("killer.inf", Inf(Killer, "11/01/2026", "25.0.0.1")),
            ("realtek.inf", Inf(Realtek, "11/04/2026", "10.0.0.1")));
        var devices = new[]
        {
            Device(Nvidia, "32.0.0.1", Installed(2026, 1, 1)),
            Device(Killer, "24.0.0.1", Installed(2026, 1, 1)),
            Device(Realtek, "9.0.0.1", Installed(2026, 1, 1))
        };
        var result = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Equal(3, result.TotalMatchedDevices);
        Assert.Equal(2, result.PotentialNewerDevices);
        Assert.Contains(result.Findings, f =>
            f.DriverVerDateRelation == LocalOemDriverComparison.InfDateRelation.Older);
        Assert.All(result.Findings, f =>
            Assert.True(f.HasExactHardwareId));
        Assert.False(result.IsInstallable);
    }
}
