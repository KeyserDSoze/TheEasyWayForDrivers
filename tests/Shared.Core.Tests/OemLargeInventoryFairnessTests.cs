using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Large OEM packs: cap the display, never the bounded matching inventory.
/// Exact PnP evidence is read-only and cannot become an installable offer.
/// </summary>
public sealed class OemLargeInventoryFairnessTests
{
    private const string NvidiaGpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaAudio = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string KillerWifi = @"PCI\VEN_8086&DEV_51F1&SUBSYS_11118086";
    private const string RealtekAudio = @"INTELAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(
        string instanceId, string name, string hardwareId, string deviceClass,
        string installedVersion = "31.0.0.0",
        IReadOnlyList<string>? compatible = null) =>
        new(instanceId, name, "Microsoft", deviceClass, "Microsoft",
            installedVersion, null, "oem3.inf", true, true, 0,
            [hardwareId], compatible ?? []);

    private static DriverInfo[] IntelFirstInventory(int count,
        string installedVersion = "32.0.15.7000") =>
        Enumerable.Range(0, count)
            .Select(index => Device(
                "SECRET_INTEL_INSTANCE_" + index,
                "Network Controller " + index,
                $@"PCI\VEN_8086&DEV_{index:X4}", "Net",
                installedVersion))
            .ToArray();

    private static string Inf(
        IEnumerable<string> ids,
        string version = "32.0.15.7000",
        string decoration = "NTamd64.10.0...22000")
    {
        var declarations = ids.Select((id, index) =>
            $"Model{index}=Install,{id}");
        return "[Version]\nSignature=\"$Windows NT$\"\n" +
            $"DriverVer=10/04/2026,{version}\n" +
            "[Manufacturer]\nVendor=Models," + decoration + "\n" +
            $"[Models.{decoration}]\n" +
            string.Join("\n", declarations) + "\n";
    }

    private static MemoryStream Zip(params (string Path, string Text)[] files)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (path, contents) in files)
            {
                var item = zip.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(item.Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        memory.Position = 0;
        return memory;
    }

    [Fact]
    public void MoreThan80IntelModelsCannotHideLastNvidiaGpuAudioKillerAndRealtek()
    {
        var intel = IntelFirstInventory(120);
        var devices = intel.Concat(
        [
            Device("PRIVATE_NVIDIA_GPU", "Microsoft Basic Display Adapter",
                NvidiaGpu, "Display", "31.0.15.2000"),
            Device("PRIVATE_NVIDIA_AUDIO", "High Definition Audio Device",
                NvidiaAudio, "MEDIA", "31.0.15.2000"),
            Device("PRIVATE_KILLER_WIFI", "Killer Wi-Fi Network Controller",
                KillerWifi, "Net", "23.0.1.1"),
            Device("PRIVATE_REALTEK_CODEC", "High Definition Audio Device",
                RealtekAudio, "MEDIA", "6.0.1.1")
        ]).ToArray();

        // All Intel models are declared BEFORE the other four. The old
        // first-80 collector silently discarded all four later devices.
        var inf = Inf(devices.Select(d => d.HardwareIds[0]));
        using var archive = Zip(("large/oem.inf", inf));
        var result = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);

        Assert.Equal(124, result.TotalMatchedDevices);
        Assert.Equal(LocalOemDriverComparison.MaximumDisplayedFindings,
            result.DevicesWithEvidence);
        Assert.Equal(44, result.MatchingDevicesOmitted);
        Assert.Equal(44, result.AdditionalMatchesOmitted);
        Assert.Contains(result.Findings, x => x.DeviceName == "Microsoft Basic Display Adapter"
            && x.IsPotentialNewer);
        Assert.Contains(result.Findings, x => x.DeviceName == "Killer Wi-Fi Network Controller");
        Assert.Contains(result.Findings, x => x.DeviceName == "High Definition Audio Device"
            && x.IsPotentialNewer);
        Assert.Contains(result.Notes, x => x.Contains("44 altri dispositivi"));
        Assert.Contains("124 dispositivi con corrispondenze PnP", result.Summary);
        Assert.Contains("80 mostrati, 44 oltre il limite", result.Summary);
        Assert.DoesNotContain("PRIVATE_", result.Summary);
        Assert.DoesNotContain(@"PCI\VEN", result.Summary);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void VendorFairnessPreservesOlderNvidiaWhenIntelHas100NewerHints()
    {
        var intel = IntelFirstInventory(110, "21.0.0.0");
        var nvidia = Device("PRIVATE_GPU", "Basic Display Adapter",
            NvidiaGpu, "Display", "99.0.0.0");
        var devices = intel.Append(nvidia).ToArray();
        using var archive = Zip(("drivers.inf",
            Inf(devices.Select(d => d.HardwareIds[0]))));

        var result = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Equal(111, result.TotalMatchedDevices);
        Assert.Equal(31, result.MatchingDevicesOmitted);
        Assert.Contains(result.Findings, row =>
            row.DeviceName == "Basic Display Adapter" &&
            row.VersionComparison == LocalOemDriverComparison.VersionRelation.OlderHint &&
            !row.IsPotentialNewer);
        Assert.Equal(79, result.PotentialNewerDevices);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void ReorderedInventoryAndInfEntriesYieldSameDisplayRows()
    {
        var intel = IntelFirstInventory(104);
        var special = new[]
        {
            Device("PRIVATE_GPU", "Microsoft Basic Display Adapter",
                NvidiaGpu, "Display", "31.0.15.6000"),
            Device("PRIVATE_KILLER", "Killer Wi-Fi Network Controller",
                KillerWifi, "Net", "23.0.0.0")
        };
        var devices = intel.Concat(special).ToArray();
        var ids = devices.Select(device => device.HardwareIds[0]).ToArray();
        using var forward = Zip(("large/driver.inf", Inf(ids)));
        using var reverse = Zip(("large/driver.inf", Inf(ids.Reverse())));

        var one = LocalOemDriverComparison.Compare(
            forward, devices, "x64", 10, 0, 26100);
        var two = LocalOemDriverComparison.Compare(
            reverse, devices.Reverse().ToArray(), "x64", 10, 0, 26100);

        Assert.Equal(one.TotalMatchedDevices, two.TotalMatchedDevices);
        Assert.Equal(one.MatchingDevicesOmitted, two.MatchingDevicesOmitted);
        Assert.Equal(
            one.Findings.Select(f => f.Summary).ToArray(),
            two.Findings.Select(f => f.Summary).ToArray());
    }

    [Fact]
    public void CompatibleOnlyOrWrongWindowsNeverClaimsNewerAmongOverflow()
    {
        var intel = IntelFirstInventory(90);
        var gpu = Device("PRIVATE_GPU", "Basic Display Adapter",
            NvidiaGpu, "Display", "31.0.0.0",
            [@"PCI\VEN_10DE&DEV_28A0"]);
        var list = intel.Append(gpu).ToArray();

        // The INF declares only the generic GPU Compatible ID.
        var allIds = intel.Select(device => device.HardwareIds[0])
            .Append(@"PCI\VEN_10DE&DEV_28A0");
        using var compatArchive = Zip(("driver.inf", Inf(allIds)));
        var compat = LocalOemDriverComparison.Compare(
            compatArchive, list, "x64", 10, 0, 26100);
        Assert.Contains(compat.Findings, finding =>
            finding.DeviceName == "Basic Display Adapter" &&
            !finding.HasExactHardwareId &&
            !finding.IsPotentialNewer);
        Assert.Equal(0, compat.PotentialNewerDevices);

        using var wrongArch = Zip(("driver.inf",
            Inf(list.Select(device => device.HardwareIds[0]),
                decoration: "NTarm64.10.0...26100")));
        var mismatch = LocalOemDriverComparison.Compare(
            wrongArch, list, "x64", 10, 0, 26100);
        Assert.Equal(0, mismatch.PotentialNewerDevices);
        Assert.All(mismatch.Findings, finding =>
            Assert.Equal(InfPlatformPreflight.Verdict.ArchitectureMismatch,
                finding.PlatformVerdict));
        Assert.False(mismatch.IsInstallable);
        Assert.False(mismatch.IsVerifiedCompatible);
    }

    [Fact]
    public void ExtractedFolderUsesTheSameFairLimitAndDoesNotLoseLateGpu()
    {
        var intel = IntelFirstInventory(90);
        var nvidia = Device("PRIVATE_GPU", "Basic Display Adapter",
            NvidiaGpu, "Display", "31.0.0.0");
        var devices = intel.Append(nvidia).ToArray();
        var temp = Directory.CreateTempSubdirectory("OmegaDrive-FairOem-");
        try
        {
            File.WriteAllText(Path.Combine(temp.FullName, "driver.inf"),
                Inf(devices.Select(device => device.HardwareIds[0])),
                new UTF8Encoding(false));

            var report = LocalOemFolderComparison.Compare(
                temp.FullName, devices, "x64", 10, 0, 26100);
            Assert.Equal(91, report.TotalMatchedDevices);
            Assert.Equal(80, report.DevicesWithEvidence);
            Assert.Equal(11, report.MatchingDevicesOmitted);
            Assert.Contains(report.Findings, row => row.DeviceName ==
                "Basic Display Adapter" && row.IsPotentialNewer);
            Assert.Contains(report.Notes, note => note.Contains("sola lettura"));
            Assert.False(report.IsInstallable);
            Assert.False(report.IsVerifiedCompatible);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void DuplicateInfForSameHardwareStillCountsOneDeviceAndOneExtraMatch()
    {
        var intel = IntelFirstInventory(90);
        var nvidia = Device("PRIVATE_GPU", "Basic Display Adapter",
            NvidiaGpu, "Display", "31.0.0.0");
        var devices = intel.Append(nvidia).ToArray();
        using var archive = Zip(
            ("one/all.inf", Inf(devices.Select(device => device.HardwareIds[0]))),
            ("two/nvidia.inf", Inf([NvidiaGpu], "33.0.0.0")));

        var report = LocalOemDriverComparison.Compare(
            archive, devices, "x64", 10, 0, 26100);
        Assert.Equal(91, report.TotalMatchedDevices);
        Assert.Equal(80, report.DevicesWithEvidence);
        Assert.Equal(11, report.MatchingDevicesOmitted);
        Assert.Equal(12, report.AdditionalMatchesOmitted);
        Assert.Contains(report.Findings, finding =>
            finding.DeviceName == "Basic Display Adapter" &&
            finding.HasExactHardwareId);
        Assert.Contains(report.Notes, note =>
            note.Contains("Più INF possono riferirsi"));
        Assert.False(report.IsInstallable);
    }
}
