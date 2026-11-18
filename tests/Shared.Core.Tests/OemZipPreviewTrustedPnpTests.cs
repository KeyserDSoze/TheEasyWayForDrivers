using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #560: ZIP INF preview, WUA audit and full OEM matching must agree
/// about contradictory or duplicated Windows PnP instances.
/// </summary>
public sealed class OemZipPreviewTrustedPnpTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string OtherNvidia = @"PCI\VEN_10DE&DEV_1E84";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string instance, string hardware,
        string name = "Windows Generic Device") =>
        new(instance, name, "Microsoft", "Unknown", "Microsoft",
            "1.0", null, "oem-private.inf", true, true, 0,
            [hardware], []);

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\nVendor=Models,NTamd64\n" +
        "[Models.NTamd64]\nDriver=Install," + id + "\n";

    private static MemoryStream Zip(params string[] ids)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create,
            leaveOpen: true))
            for (var i = 0; i < ids.Length; i++)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry($"oem/{i:D2}.inf").Open(),
                    new UTF8Encoding(false));
                writer.Write(Inf(ids[i]));
            }
        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OppositeVendorsAtSamePnpInstanceCannotProduceZipMatches(
        bool targeted)
    {
        var gpu = Device("PRIVATE_SERIAL", Nvidia, "NVIDIA GPU");
        var codec = Device(" private_serial ", Realtek, "Realtek Audio");
        var killer = Device("PRIVATE_KILLER", Killer, "Killer Wi-Fi");
        var inventory = new[] { gpu, codec, killer };

        using var zip = Zip(Nvidia, Realtek, Killer);
        var evidence = targeted
            ? OemZipInfEvidence.Inspect(zip, inventory)
            : OemZipInfEvidence.Inspect(zip);
        var result = OemHardwareEvidence.Compare(evidence, inventory);
        Assert.Equal(3, result.DevicesExamined);
        Assert.Equal(1, result.TrustedDevices);
        Assert.Equal(1, result.ExcludedUnverifiedPnpIdentities);
        var match = Assert.Single(result.Matches);
        Assert.Equal(Killer, match.DeclaredId);
        Assert.Equal(OemHardwareEvidence.Strength.HardwareId, match.Level);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoNvidiaModelsOfSameVendorStillConflict(bool reversed)
    {
        var gpu = Device("PRIVATE_GPU", Nvidia);
        var other = Device(" private_gpu ",
            OtherNvidia);
        var rows = reversed ? new[] { other, gpu } : new[] { gpu, other };
        using var zip = Zip(Nvidia, OtherNvidia);
        var report = OemHardwareEvidence.Compare(
            OemZipInfEvidence.Inspect(zip, rows), rows);
        Assert.Empty(report.Matches);
        Assert.Equal(0, report.TrustedDevices);
        Assert.Equal(1, report.ExcludedUnverifiedPnpIdentities);
    }

    [Fact]
    public void ConflictingSubsystemsAreNotExactInfEvidence()
    {
        var gpu = Device("PRIVATE_GPU",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA");
        var other = Device(" private_gpu ",
            @"PCI\VEN_10DE&DEV_28A0&SUBSYS_000010DE");
        using var zip = Zip(gpu.HardwareIds[0], other.HardwareIds[0]);
        var report = OemHardwareEvidence.Compare(
            OemZipInfEvidence.Inspect(zip), [gpu, other]);
        Assert.Empty(report.Matches);
        Assert.Equal(1, report.ExcludedUnverifiedPnpIdentities);
    }

    [Fact]
    public void DuplicatedCaseAndAsciiPaddedDeviceCountsOnce()
    {
        var first = Device("PRIVATE_GPU", Nvidia);
        var duplicate = first with
        {
            DeviceId = " private_gpu ",
            DriverProvider = "NVIDIA"
        };
        using var zip = Zip(Nvidia);
        var report = OemHardwareEvidence.Compare(
            OemZipInfEvidence.Inspect(zip), [first, duplicate]);
        Assert.Equal(2, report.DevicesExamined);
        Assert.Equal(1, report.TrustedDevices);
        Assert.Equal(0, report.ExcludedUnverifiedPnpIdentities);
        Assert.Single(report.Matches);
    }

    [Fact]
    public void SingleRowWithContradictoryPrimaryModelsIsNotTrusted()
    {
        var bad = Device("PRIVATE_SHARED", Nvidia) with
        {
            HardwareIds = [Nvidia, Killer]
        };
        using var zip = Zip(Nvidia, Killer);
        var result = OemHardwareEvidence.Compare(
            OemZipInfEvidence.Inspect(zip), [bad]);
        Assert.Empty(result.Matches);
        Assert.Equal(1, result.ExcludedUnverifiedPnpIdentities);
    }

    [Fact]
    public void MissingInstanceCannotClaimExactMatchAndIsCounted()
    {
        var unknown = Device("", Nvidia);
        var healthy = Device("PRIVATE_KILLER", Killer);
        using var zip = Zip(Nvidia, Killer);
        var result = OemHardwareEvidence.Compare(
            OemZipInfEvidence.Inspect(zip), [unknown, healthy]);
        Assert.Equal(2, result.DevicesExamined);
        Assert.Equal(1, result.TrustedDevices);
        Assert.Equal(1, result.ExcludedUnverifiedPnpIdentities);
        Assert.Equal(Killer, Assert.Single(result.Matches).DeclaredId);
    }

    [Fact]
    public void MoreThan262144PnpIdsFailsBeforeGeneratingInfEvidence()
    {
        var ids = Enumerable.Repeat(Nvidia,
            (int)VendorInventoryCoverage.MaximumPnpIds + 1).ToArray();
        var bad = Device("PRIVATE_TOO_MANY", Nvidia) with
        {
            HardwareIds = ids
        };
        using var zip = Zip(Nvidia);
        Assert.Throws<InvalidDataException>(() =>
            OemHardwareEvidence.Compare(OemZipInfEvidence.Inspect(zip), [bad]));
    }

    [Fact]
    public void MoreThan2048RawPnpRowsStopsEnumeratingEarly()
    {
        var reads = 0;
        IEnumerable<DriverInfo> Infinite()
        {
            while (true)
            {
                if (++reads > 2049)
                    throw new InvalidOperationException("Unexpected PnP enumeration");
                yield return Device("PRIVATE_" + reads, Nvidia);
            }
        }
        using var zip = Zip(Nvidia);
        var evidence = OemZipInfEvidence.Inspect(zip);
        Assert.Throws<InvalidDataException>(() =>
            OemHardwareEvidence.Compare(evidence, Infinite()));
        Assert.Equal(2049, reads);
    }

    [Fact]
    public void TargetedZipAfterPreview500KeepsHealthyModelOnly()
    {
        var many = string.Join("\n", Enumerable.Range(0, 550)
            .Select(i => $@"Extra{i}=Install,PCI\VEN_9999&DEV_{i:X4}"));
        var longInf = Inf(Nvidia) +
            "\n[Models.NTamd64]\n" + many +
            "\nRealtek=Install," + Realtek +
            "\nKiller=Install," + Killer;
        var gpu = Device("PRIVATE_BAD", Nvidia);
        var codec = Device(" private_bad ", Realtek);
        var killer = Device("PRIVATE_GOOD", Killer);
        var inventory = new[] { gpu, codec, killer };
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            using var writer = new StreamWriter(
                archive.CreateEntry("late.inf").Open(),
                new UTF8Encoding(false));
            writer.Write(longInf);
        }
        zip.Position = 0;
        var targeted = OemZipInfEvidence.Inspect(zip, inventory);
        var report = OemHardwareEvidence.Compare(targeted, inventory);
        Assert.True(report.UsedFullTargetedMatching);
        Assert.Equal(1, report.ExcludedUnverifiedPnpIdentities);
        Assert.Equal(Killer, Assert.Single(report.Matches).DeclaredId);
        Assert.False(report.IsInstallable);
    }
}
