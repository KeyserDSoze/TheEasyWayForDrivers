using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class InfTargetOsSelectionTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
    private const string Realtek = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string instance, string hardware,
        string version = "31.0.0.0") =>
        new(instance, "Generic Windows device", "Generic vendor",
            "Display", "Windows", version, null, "oem-private.inf",
            true, true, 0, [hardware], []);

    private static string Inf(string olderModels, string newerModels,
        string manufacturer = "%Vendor%=Models,NTamd64.10.0...22000,NTamd64.10.0...26100") =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "CatalogFile.NTamd64=driver.cat\n" +
        "DriverVer=11/08/2026,32.0.16.9500\n" +
        "[Manufacturer]\n" + manufacturer + "\n" +
        "[Models.NTamd64.10.0...22000]\n" + olderModels + "\n" +
        "[Models.NTamd64.10.0...26100]\n" + newerModels + "\n";

    private static MemoryStream Zip(string name, string text)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(Nvidia)]
    [InlineData(Killer)]
    [InlineData(Realtek)]
    public void NewerEmptyModelSectionShadowsOlderNvidiaKillerRealtekIds(
        string hardware)
    {
        var inf = Inf("%Device%=Install," + hardware, "");
        var device = Device("PRIVATE_INSTANCE", hardware);

        // All sections remain visible as informational INF declarations,
        // but Windows picks only one TargetOSVersion per Manufacturer line.
        Assert.Single(InfModelEvidence.ReadMatchingIds(inf, [device]));

        var oldBuild = InfPlatformPreflight.Assess(
            inf, device, "amd64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            oldBuild.Result);
        Assert.Equal(2, oldBuild.HardwareMatchStrength);

        var newBuild = InfPlatformPreflight.Assess(
            inf, device, "amd64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            newBuild.Result);
        Assert.Equal(0, newBuild.HardwareMatchStrength);
        Assert.Contains("sostituita", newBuild.Explanation);
        Assert.DoesNotContain("PRIVATE_INSTANCE", newBuild.Explanation);
    }

    [Fact]
    public void OlderSectionIsNotUsedWhenNewerSectionHasDifferentDevice()
    {
        var inf = Inf("%Nvidia%=Install," + Nvidia,
            "%Other%=Install,PCI\\VEN_9999&DEV_0001");
        var outcome = InfPlatformPreflight.Assess(
            inf, Device("PRIVATE_GPU", Nvidia), "x64", 10, 0, 26100,
            catalogFileExists: true);

        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            outcome.Result);
        Assert.Equal(0, outcome.HardwareMatchStrength);
        Assert.Contains("NON verificata", outcome.CatalogStatus);
    }

    [Fact]
    public void NewerCompatibleAliasCannotInheritPrimaryNvidiaMatchFromOldSection()
    {
        var inf = Inf("%Nvidia%=Install," + Nvidia,
            "%Other%=Install,PCI\\VEN_9999&DEV_9999," + Nvidia);
        var result = InfPlatformPreflight.Assess(
            inf, Device("PRIVATE_GPU", Nvidia), "amd64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            result.Result);
        Assert.Equal(1, result.HardwareMatchStrength);
        Assert.Contains("Compatible ID", result.Explanation);
        Assert.DoesNotContain("firma valida", result.CatalogStatus);

        using var archive = Zip("oem/gpu.inf", inf);
        var comparison = LocalOemDriverComparison.Compare(
            archive, [Device("PRIVATE_GPU", Nvidia)], "amd64", 10, 0, 26100);
        var finding = Assert.Single(comparison.Findings);
        Assert.Equal(1, finding.HardwareMatchStrength);
        Assert.False(finding.IsPotentialNewer);
        Assert.False(comparison.IsInstallable);
    }

    [Fact]
    public void IndependentManufacturerRowMayStillSelectOlderGpuSection()
    {
        var inf = Inf("%Nvidia%=Install," + Nvidia,
            "%Other%=Install,PCI\\VEN_9999&DEV_9999",
            "%Vendor%=Models,NTamd64.10.0...22000,NTamd64.10.0...26100\n" +
            "%SecondVendor%=Models,NTamd64.10.0...22000");
        var result = InfPlatformPreflight.Assess(
            inf, Device("PRIVATE_GPU", Nvidia), "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            result.Result);
        Assert.Equal(2, result.HardwareMatchStrength);
    }

    [Fact]
    public void Arm64SectionNeverShadowsAmd64Models()
    {
        var inf = """
            [Version]
            Signature="$Windows NT$"
            DriverVer=11/08/2026,32.0.16.9500
            [Manufacturer]
            %Vendor%=Models,NTamd64.10.0...22000,NTarm64.10.0...26100
            [Models.NTamd64.10.0...22000]
            %Gpu%=Install,PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA
            [Models.NTarm64.10.0...26100]
            %Other%=Install,PCI\VEN_9999&DEV_0001
            """;
        var report = InfPlatformPreflight.Assess(
            inf, Device("PRIVATE_GPU", Nvidia), "AMD64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            report.Result);
        Assert.Equal(2, report.HardwareMatchStrength);
    }

    [Fact]
    public void UnknownProductTypeCanShadowOlderModelSoReviewIsMandatory()
    {
        var inf = Inf("%Nvidia%=Install," + Nvidia,
            "%Other%=Install,PCI\\VEN_9999&DEV_9999",
            "%Vendor%=Models,NTamd64.10.0...22000,NTamd64.10.0.3..26100");
        var report = InfPlatformPreflight.Assess(
            inf, Device("PRIVATE_GPU", Nvidia), "AMD64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired,
            report.Result);
        Assert.Equal(0, report.HardwareMatchStrength);
        Assert.Contains("non", report.Explanation);
    }

    [Fact]
    public void OemZipCannotClaimThreeNewerDriversFromShadowedOldSection()
    {
        var older = "%Gpu%=Install," + Nvidia + "\n" +
            "%Wifi%=Install," + Killer + "\n" +
            "%Audio%=Install," + Realtek;
        var inf = Inf(older, "");
        var devices = new[]
        {
            Device("PRIVATE_NVIDIA", Nvidia),
            Device("PRIVATE_KILLER", Killer),
            Device("PRIVATE_REALTEK", Realtek)
        };
        using var zip = Zip("official/recoil.inf", inf);
        var current = LocalOemDriverComparison.Compare(
            zip, devices, "x64", 10, 0, 26100);
        Assert.Equal(1, current.InfExamined);
        Assert.Empty(current.Findings);
        Assert.Equal(0, current.PotentialNewerDevices);
        Assert.False(current.IsInstallable);

        zip.Position = 0;
        var olderWindows = LocalOemDriverComparison.Compare(
            zip, devices, "x64", 10, 0, 22631);
        Assert.Equal(3, olderWindows.TotalMatchedDevices);
        Assert.Equal(3, olderWindows.PotentialNewerDevices);
        Assert.False(olderWindows.IsInstallable);
        Assert.DoesNotContain("PRIVATE_", olderWindows.Summary);
    }

    [Fact]
    public void ExtractedFolderUsesSameWindowsTargetOsSpecificity()
    {
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-TargetOS-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "gpu.inf"),
                Inf("%Gpu%=Install," + Nvidia, ""));
            var newBuild = LocalOemFolderComparison.Compare(
                folder.FullName, [Device("PRIVATE_GPU", Nvidia)],
                "AMD64", 10, 0, 26100);
            Assert.Empty(newBuild.Findings);
            Assert.Equal(0, newBuild.PotentialNewerDevices);

            var oldBuild = LocalOemFolderComparison.Compare(
                folder.FullName, [Device("PRIVATE_GPU", Nvidia)],
                "AMD64", 10, 0, 22631);
            Assert.Single(oldBuild.Findings);
            Assert.Equal(1, oldBuild.PotentialNewerDevices);
            Assert.DoesNotContain(folder.FullName, newBuild.Summary);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
