using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #496: an independently valid PnP/INF Hardware ID cannot attest an
/// installed driver version when the SignedDriver metadata check failed.
/// </summary>
public sealed class LocalOemUnverifiedInstalledMetadataTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private static readonly DateTimeOffset LastInstalled =
        new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static DriverInfo Device(string id,
        bool hasDriver = true, bool metadataSucceeded = true,
        string version = "25.0.0.1") =>
        new(id + @"\PRIVATE_INSTANCE_999",
            "Microsoft Basic Device", "Microsoft", "System",
            "Microsoft", version, LastInstalled, "oem12.inf",
            true, hasDriver, 0, [id], [],
            DriverMetadataQuerySucceeded: metadataSucceeded);

    private static string Inf(string hardwareId,
        string version = "32.0.0.1", string date = "11/2/2026") => $"""
        [Version]
        Signature="$Windows NT$"
        Class=System
        DriverVer={date},{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0
        [Models.NTamd64.10.0]
        %Device%=Install,{hardwareId}
        """;

    private static MemoryStream Zip(
        params (string Path, string Content)[] entries)
    {
        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(
                    archive.CreateEntry(name).Open(),
                    new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        output.Position = 0;
        return output;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void StaleInstalledMetadataCannotYieldNewerNvidiaDriver(
        bool hasDriver, bool metadataSucceeded)
    {
        using var archive = Zip(("nvidia.inf", Inf(Nvidia)));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia, hasDriver, metadataSucceeded)],
            "x64", 10, 0, 26100);

        var finding = Assert.Single(report.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            finding.PlatformVerdict);
        Assert.False(finding.InstalledDriverMetadataVerified);
        Assert.Equal("non verificata", finding.InstalledVersion);
        Assert.Equal("32.0.0.1", finding.PackageVersion);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            finding.VersionComparison);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.NotVerified,
            finding.DriverVerDateRelation);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Contains("Metadati del driver Windows non verificati",
            finding.Summary);
        Assert.DoesNotContain("25.0.0.1", finding.Summary);
        Assert.DoesNotContain("PRIVATE_INSTANCE", finding.Summary);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void VerifiedDriverMetadataStillSupportsConservativeNewerHint()
    {
        using var archive = Zip(("nvidia.inf", Inf(Nvidia)));
        var report = LocalOemDriverComparison.Compare(archive,
            [Device(Nvidia)], "x64", 10, 0, 26100);
        var finding = Assert.Single(report.Findings);
        Assert.True(finding.InstalledDriverMetadataVerified);
        Assert.Equal("25.0.0.1", finding.InstalledVersion);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            finding.VersionComparison);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.Newer,
            finding.DriverVerDateRelation);
        Assert.True(finding.IsPotentialNewer);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void NvidiaKillerRealtekAllRemainIdentifiedWithoutVersionEvidence()
    {
        using var archive = Zip(
            ("gpu.inf", Inf(Nvidia)),
            ("network.inf", Inf(Killer)),
            ("audio.inf", Inf(Realtek)));
        var devices = new[]
        {
            Device(Nvidia, metadataSucceeded: false),
            Device(Killer, hasDriver: false),
            Device(Realtek)
        };
        var result = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);

        Assert.Equal(3, result.TotalMatchedDevices);
        Assert.Equal(3, result.PnpMatchedDeviceOrdinals!.Count);
        Assert.Equal(1, result.PotentialNewerDevices);
        Assert.Equal(2, result.Findings.Count(f =>
            !f.InstalledDriverMetadataVerified));
        Assert.Equal(2, result.Findings.Count(f =>
            f.VersionComparison == LocalOemDriverComparison.VersionRelation.Unavailable));
        Assert.All(result.Findings, finding =>
        {
            Assert.True(finding.HasExactHardwareId);
            Assert.False(result.IsInstallable);
        });
    }

    [Fact]
    public void TwoMatchedInfsCannotResurrectStaleInstalledVersion()
    {
        using var archive = Zip(
            ("old/gpu.inf", Inf(Nvidia, "31.0.0.1", "1/1/2021")),
            ("new/gpu.inf", Inf(Nvidia, "99.0.0.1", "11/2/2026")));
        var result = LocalOemDriverComparison.Compare(
            archive, [Device(Nvidia, metadataSucceeded: false)],
            "x64", 10, 0, 26100);

        Assert.Single(result.Findings);
        Assert.Equal(1, result.AdditionalMatchesOmitted);
        Assert.Equal(1, result.TotalMatchedDevices);
        Assert.Equal(0, result.PotentialNewerDevices);
        var visible = Assert.Single(result.Findings);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            visible.VersionComparison);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.NotVerified,
            visible.DriverVerDateRelation);
        Assert.Equal("non verificata", visible.InstalledVersion);
        Assert.False(visible.IsPotentialNewer);
    }

    [Fact]
    public void LocalFolderPathUsesSameVerifiedMetadataGate()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "omegadrive-496-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "nvidia.inf"),
                Inf(Nvidia), new UTF8Encoding(false));
            var result = LocalOemFolderComparison.Compare(directory,
                [Device(Nvidia, metadataSucceeded: false)],
                "x64", 10, 0, 26100);
            var finding = Assert.Single(result.Findings);
            Assert.True(finding.HasExactHardwareId);
            Assert.False(finding.InstalledDriverMetadataVerified);
            Assert.Equal(0, result.PotentialNewerDevices);
            Assert.False(result.IsInstallable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
