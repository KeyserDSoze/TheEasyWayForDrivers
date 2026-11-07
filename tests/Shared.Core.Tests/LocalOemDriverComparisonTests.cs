using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class LocalOemDriverComparisonTests
{
    private static DriverInfo Device(
        string name, string hardwareId, string version,
        string[]? compatibles = null) =>
        new(hardwareId + @"\INSTANCE_PRIVATE_123",
            name, "NVIDIA", "Display", "NVIDIA", version, null,
            "oem20.inf", true, true, 0, [hardwareId], compatibles ?? []);

    private static string Inf(
        string id,
        string version = "32.0.15.7000",
        string decoration = "NTamd64.10.0...22000") =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        CatalogFile.NTamd64=verified.cat
        DriverVer=10/04/2026,{version}
        [Manufacturer]
        %Vendor%=Models,{decoration}
        [Models.{decoration}]
        %Device%=Install,{id}
        """;

    private static MemoryStream MakeZip(params (string Path, string Contents)[] entries)
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        archive.Position = 0;
        return archive;
    }

    [Fact]
    public void RecoilGpuInfProducesOnePotentialVersionHintNotInstallableUpdate()
    {
        const string hardware = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        using var archive = MakeZip(
            ("graphics/driver.inf", Inf(hardware)),
            ("graphics/verified.cat", "a fake catalog file"));
        var pc = Device("NVIDIA GeForce RTX 4060 Laptop GPU", hardware,
            "31.0.15.5222");
        var report = LocalOemDriverComparison.Compare(
            archive, [pc], "x64", 10, 0, 26100);

        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.DevicesExamined);
        var candidate = Assert.Single(report.Findings);
        Assert.True(candidate.HasExactHardwareId);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            candidate.PlatformVerdict);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            candidate.VersionComparison);
        Assert.True(candidate.IsPotentialNewer);
        Assert.Equal("32.0.15.7000", candidate.PackageVersion);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.Contains("NON certifica firma", report.Summary);
        Assert.DoesNotContain("INSTANCE_PRIVATE", report.Summary);
    }

    [Fact]
    public void WindowsZipPathsRecoverNvidiaKillerAndRealtekWithoutFalseDuplicates()
    {
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        const string killer = @"PCI\VEN_8086&DEV_51F1";
        const string audio = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        using var archive = MakeZip(
            (@"drivers\NVIDIA\gpu.INF", Inf(gpu)),
            (@"drivers\Killer\network.inf", Inf(killer, "24.80.0.2")),
            (@"drivers\Audio\realtek.inf", Inf(audio, "10.0.0.1")),
            ("drivers/aux/old-gpu.inf", Inf(gpu, "31.0.0.1")));
        var devices = new[]
        {
            Device("NVIDIA RTX GPU", gpu, "31.0.0.0"),
            Device("Killer Wi-Fi", killer, "23.0.0.0"),
            Device("Realtek Audio", audio, "9.0.0.0")
        };
        var report = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);

        Assert.Equal(4, report.InfExamined);
        Assert.Equal(3, report.TotalMatchedDevices);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.Equal(3, report.MatchedDeviceOrdinals!.Distinct().Count());
        Assert.Contains(report.Findings, f =>
            f.DeviceName == "NVIDIA RTX GPU" &&
            f.PackageVersion == "32.0.15.7000");
        Assert.Contains(report.Findings, f =>
            f.DeviceName == "Killer Wi-Fi" && f.HasExactHardwareId);
        Assert.Contains(report.Findings, f =>
            f.DeviceName == "Realtek Audio" && f.HasExactHardwareId);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void OneArchiveCanCorrelateDistinctNvidiaGpuAndAudioDevices()
    {
        const string gpu = @"PCI\VEN_10DE&DEV_28A0";
        const string audio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
        using var archive = MakeZip(
            ("gpu/display.inf", Inf(gpu)),
            ("audio/audio.inf", Inf(audio)));
        var devices = new[]
        {
            Device("NVIDIA GPU", gpu, "31.0.15.1111"),
            Device("NVIDIA Audio", audio, "1.1.1.1"),
            Device("Intel Killer WiFi", @"PCI\VEN_8086&DEV_51F1", "23.0.1.1")
        };

        var result = LocalOemDriverComparison.Compare(
            archive, devices, "AMD64", 10, 0, 26100);
        Assert.Equal(3, result.DevicesExamined);
        Assert.Equal(2, result.DevicesWithEvidence);
        Assert.Equal(2, result.PotentialNewerDevices);
        Assert.DoesNotContain(result.Findings, item =>
            item.DeviceName.Contains("Killer"));
    }

    [Fact]
    public void SameDeviceInMultipleInfFilesIsNeverDoubleCounted()
    {
        const string id = @"PCI\VEN_10DE&DEV_28A0";
        using var archive = MakeZip(
            ("one/graphics.inf", Inf(id, "31.0.0.1")),
            ("two/graphics.inf", Inf(id, "32.0.0.1")));
        var result = LocalOemDriverComparison.Compare(
            archive, [Device("RTX GPU", id, "30.0.0.1")],
            "x64", 10, 0, 26100);
        Assert.Single(result.Findings);
        Assert.Equal(1, result.AdditionalMatchesOmitted);
        Assert.Contains("Più INF", string.Join(" ", result.Notes));
    }

    [Theory]
    [InlineData("NTarm64.10.0", 10, 0, 26100,
        InfPlatformPreflight.Verdict.ArchitectureMismatch)]
    [InlineData("NTamd64.10.0...30000", 10, 0, 26100,
        InfPlatformPreflight.Verdict.MinimumWindowsNotMet)]
    public void UnsupportedArchitectureOrOsCannotBeCalledNewDriver(
        string decoration, int major, int minor, int build,
        InfPlatformPreflight.Verdict expected)
    {
        const string id = @"PCI\VEN_10DE&DEV_28A0";
        using var archive = MakeZip(("gpu.inf", Inf(id, decoration: decoration)));
        var result = LocalOemDriverComparison.Compare(
            archive, [Device("GPU", id, "31.0.15.4000")],
            "x64", major, minor, build);
        var candidate = Assert.Single(result.Findings);
        Assert.Equal(expected, candidate.PlatformVerdict);
        Assert.False(candidate.IsPotentialNewer);
        Assert.Equal(0, result.PotentialNewerDevices);
    }

    [Fact]
    public void MatchingOnlyCompatibleIdIsNotAConfidentNewVersion()
    {
        const string generic = @"PCI\VEN_10DE&DEV_28A0";
        const string specific = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
        using var archive = MakeZip(("graphics.inf", Inf(generic)));
        var result = LocalOemDriverComparison.Compare(
            archive,
            [Device("GPU", specific, "31.0.15.4000", [generic])],
            "x64", 10, 0, 26100);
        var row = Assert.Single(result.Findings);
        Assert.False(row.HasExactHardwareId);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, result.PotentialNewerDevices);
    }

    [Fact]
    public void OlderOrEqualInfDoesNotCreateAnotherNewDriver()
    {
        const string id = @"PCI\VEN_8086&DEV_51F1";
        using var older = MakeZip(("wifi.inf", Inf(id, "22.0.0.0")));
        using var equal = MakeZip(("wifi.inf", Inf(id, "23.0.0.0")));
        var pc = Device("Killer WiFi", id, "23.0.0.0");
        var olderResult = LocalOemDriverComparison.Compare(
            older, [pc], "x64", 10, 0, 26100);
        var equalResult = LocalOemDriverComparison.Compare(
            equal, [pc], "x64", 10, 0, 26100);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.OlderHint,
            Assert.Single(olderResult.Findings).VersionComparison);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Equal,
            Assert.Single(equalResult.Findings).VersionComparison);
        Assert.Equal(0, olderResult.PotentialNewerDevices);
        Assert.Equal(0, equalResult.PotentialNewerDevices);
    }

    [Fact]
    public void CompletelyUnrelatedInfProducesNoDeviceMatches()
    {
        using var archive = MakeZip(("intel.inf",
            Inf(@"PCI\VEN_8086&DEV_46A6")));
        var report = LocalOemDriverComparison.Compare(
            archive, [Device("RTX", @"PCI\VEN_10DE&DEV_28A0", "31.0.1.1")],
            "x64", 10, 0, 26100);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Equal(1, report.InfExamined);
    }

    [Fact]
    public void CabinetOrInstallerCannotBeMistakenForEmptySafeZip()
    {
        using var cab = new MemoryStream(
            [0x4d, 0x53, 0x43, 0x46, 0, 0, 0, 0]);
        Assert.Throws<NotSupportedException>(() =>
            LocalOemDriverComparison.Compare(cab, [], "x64", 10, 0, 26100));
    }

    [Fact]
    public void TraversalInZipIsRejectedBeforeInfMatching()
    {
        using var archive = MakeZip(("../private/driver.inf",
            Inf(@"PCI\VEN_10DE&DEV_28A0")));
        Assert.Throws<InvalidDataException>(() =>
            LocalOemDriverComparison.Compare(archive, [], "x64", 10, 0, 26100));
    }

    [Fact]
    public void ExceedingBoundedInfCountIsRejectedWithoutPartialResults()
    {
        using var archive = MakeZip(
            Enumerable.Range(0, OemZipInfEvidence.MaxInfFiles + 1)
                .Select(x => ($"drivers/d{x}.inf", Inf(@"PCI\VEN_10DE&DEV_28A0")))
                .ToArray());
        Assert.Throws<InvalidDataException>(() =>
            LocalOemDriverComparison.Compare(archive, [], "x64", 10, 0, 26100));
    }

    [Fact]
    public void MoreThan2048InventoryDevicesIsRejected()
    {
        using var archive = MakeZip(("driver.inf", Inf(@"PCI\VEN_10DE&DEV_28A0")));
        var many = Enumerable.Range(0, 2049)
            .Select(x => Device("GPU", @"PCI\VEN_10DE&DEV_28A0", "31.0.0.0"))
            .ToArray();
        Assert.Throws<InvalidDataException>(() =>
            LocalOemDriverComparison.Compare(archive, many, "x64", 10, 0, 26100));
    }
}
