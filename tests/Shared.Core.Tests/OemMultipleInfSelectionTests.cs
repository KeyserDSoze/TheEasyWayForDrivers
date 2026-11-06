using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// An OEM ZIP/CAB/folder may contain multiple INF alternatives for one PnP
/// device. The displayed row is a bounded numeric hint, never an installation
/// recommendation or Windows SetupAPI driver rank.
/// </summary>
public sealed class OemMultipleInfSelectionTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string GpuGeneric = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string name, string hardware, string installed,
        string deviceClass = "Display", IReadOnlyList<string>? compatibles = null) =>
        new("PRIVATE_INSTANCE_" + name, name, "Microsoft", deviceClass,
            "Microsoft", installed, null, "oem12.inf", true, true, 0,
            [hardware], compatibles ?? []);

    private static string Inf(string id, string version,
        string decoration = "NTamd64.10.0...22000") =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        DriverVer=10/04/2026,{version}
        [Manufacturer]
        Vendor=Models,{decoration}
        [Models.{decoration}]
        Model=Install,{id}
        """;

    private static MemoryStream Zip(params (string Path, string Inf)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (path, inf) in entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(),
                    new UTF8Encoding(false));
                writer.Write(inf);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static LocalOemDriverComparison.Report Compare(
        MemoryStream zip, params DriverInfo[] devices) =>
        LocalOemDriverComparison.Compare(zip, devices, "x64", 10, 0, 26100);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoNewerNvidiaPackagesChooseHigherFourPartVersionRegardlessOfOrder(
        bool latestHasFirstFilename)
    {
        var low = Inf(Gpu, "32.0.9.9999");
        var high = Inf(Gpu, "32.0.10.1");
        using var zip = latestHasFirstFilename
            ? Zip(("a.inf", high), ("b.inf", low))
            : Zip(("a.inf", low), ("b.inf", high));

        var result = Compare(zip, Device("RTX Laptop GPU", Gpu, "31.0.15.5000"));
        var finding = Assert.Single(result.Findings);

        Assert.Equal("32.0.10.1", finding.PackageVersion);
        Assert.True(finding.HasExactHardwareId);
        Assert.True(finding.IsPotentialNewer);
        Assert.Equal(1, result.PotentialNewerDevices);
        Assert.Equal(1, result.AdditionalMatchesOmitted);
        Assert.Equal(1, result.DevicesWithEvidence);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_INSTANCE", result.Summary);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EqualInstalledVersionBeatsOlderInfWithoutInventingAnUpdate(
        bool equalFirst)
    {
        var equal = Inf(Killer, "23.0.0.0");
        var old = Inf(Killer, "22.99.0.0");
        using var zip = equalFirst
            ? Zip(("a.inf", equal), ("b.inf", old))
            : Zip(("a.inf", old), ("b.inf", equal));
        var row = Assert.Single(Compare(zip,
            Device("Killer WiFi", Killer, "23.0.0.0", "Net")).Findings);

        Assert.Equal("23.0.0.0", row.PackageVersion);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Equal,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExactIdOlderSafePlatformBeatsWeakCompatibleNewerInf(
        bool strongFirst)
    {
        var exact = Inf(Gpu, "31.0.0.0");
        var weak = Inf(GpuGeneric, "99.0.0.0");
        using var zip = strongFirst
            ? Zip(("a.inf", exact), ("b.inf", weak))
            : Zip(("a.inf", weak), ("b.inf", exact));

        var result = Compare(zip, Device("GPU", Gpu, "31.0.15.6000",
            compatibles: [GpuGeneric]));
        var row = Assert.Single(result.Findings);

        Assert.True(row.HasExactHardwareId);
        Assert.Equal("31.0.0.0", row.PackageVersion);
        Assert.False(row.IsPotentialNewer);
        Assert.Equal(0, result.PotentialNewerDevices);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompatibleWindowsInfBeatsHigherVersionOnWrongArchitecture(
        bool wrongFirst)
    {
        var match = Inf(Gpu, "31.0.0.0");
        var wrong = Inf(Gpu, "99.0.0.0", "NTarm64.10.0...26100");
        using var zip = wrongFirst
            ? Zip(("a.inf", wrong), ("b.inf", match))
            : Zip(("a.inf", match), ("b.inf", wrong));

        var finding = Assert.Single(Compare(zip,
            Device("GPU", Gpu, "31.0.15.6000")).Findings);

        Assert.Equal("31.0.0.0", finding.PackageVersion);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            finding.PlatformVerdict);
        Assert.False(finding.IsPotentialNewer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidInfVersionCannotOverrideSyntacticallyValidEqualVersion(
        bool badFirst)
    {
        var bad = Inf(Realtek, "99.0.dev.1");
        var valid = Inf(Realtek, "6.0.9000.1");
        using var zip = badFirst
            ? Zip(("a.inf", bad), ("b.inf", valid))
            : Zip(("a.inf", valid), ("b.inf", bad));

        var result = Compare(zip, Device("Realtek Audio", Realtek,
            "6.0.9000.1", "MEDIA"));
        var row = Assert.Single(result.Findings);

        Assert.Equal("6.0.9000.1", row.PackageVersion);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Equal,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameVersionAndConfidenceUsesDeterministicInfFilename(
        bool alphaFirst)
    {
        var text = Inf(Gpu, "32.0.10.200");
        using var zip = alphaFirst
            ? Zip(("a-alpha.inf", text), ("z-final.inf", text))
            : Zip(("z-final.inf", text), ("a-alpha.inf", text));

        var row = Assert.Single(Compare(zip,
            Device("RTX", Gpu, "31.0.15.5000")).Findings);
        Assert.Equal("a-alpha.inf", row.InfName);
        Assert.True(row.IsPotentialNewer);
    }

    [Fact]
    public void DifferentVendorsAndLargeIntelInventoryRetainNvidiaNewestAndKiller()
    {
        var intel = Enumerable.Range(0, 90).Select(n => Device(
            "Intel " + n, $@"PCI\VEN_8086&DEV_{n:X4}", "32.0.20.0", "Net"))
            .ToArray();
        var gpu = Device("NVIDIA GPU", Gpu, "31.0.0.0");
        var killer = Device("Killer WiFi", Killer, "22.0.0.0", "Net");
        var realtek = Device("Realtek Audio", Realtek, "6.0.1.0", "MEDIA");
        var all = intel.Concat([gpu, killer, realtek]).ToArray();

        var allIds = string.Join("\n",
            all.Select((device, index) =>
                $"Dev{index}=Install,{device.HardwareIds[0]}"));
        var main = $"""
            [Version]
            Signature="$Windows NT$"
            DriverVer=10/04/2026,32.0.15.7000
            [Manufacturer]
            Vendor=Models,NTamd64.10.0...22000
            [Models.NTamd64.10.0...22000]
            {allIds}
            """;
        using var zip = Zip(
            ("a/nvidia-old.inf", Inf(Gpu, "32.0.16.1000")),
            ("b/oem.inf", main),
            ("c/nvidia-latest.inf", Inf(Gpu, "32.0.20.1000")));

        var result = Compare(zip, all);
        Assert.Equal(93, result.TotalMatchedDevices);
        Assert.Equal(80, result.Findings.Count);
        Assert.Equal(13, result.MatchingDevicesOmitted);
        Assert.Contains(result.Findings, row =>
            row.DeviceName == "NVIDIA GPU" &&
            row.PackageVersion == "32.0.20.1000" &&
            row.IsPotentialNewer);
        Assert.Contains(result.Findings, row => row.DeviceName == "Killer WiFi");
        Assert.Contains(result.Findings, row => row.DeviceName == "Realtek Audio");
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }
}
