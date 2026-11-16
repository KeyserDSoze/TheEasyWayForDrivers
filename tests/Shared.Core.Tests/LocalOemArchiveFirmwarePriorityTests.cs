using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #498: across separately imported OEM archives, the same device needs
/// the SAME firmware/date safety ranking already used by a single ZIP.
/// </summary>
public sealed class LocalOemArchiveFirmwarePriorityTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string NvidiaCompatible = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static DriverInfo Device(string id,
        bool metadataVerified = true,
        string installedVersion = "32.0.0.1",
        string? compatible = null) =>
        new(id + @"\PRIVATE_INSTANCE_NOT_FOR_REPORT",
            "Generic Windows Device", "Microsoft", "System",
            "Microsoft", installedVersion,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            "oem25.inf", true, true, 0,
            [id], compatible is null ? [] : [compatible],
            DriverMetadataQuerySucceeded: metadataVerified);

    private static string Inf(string id, string className,
        string date, string version) => $"""
        [Version]
        Signature="$Windows NT$"
        Class={className}
        DriverVer={date},{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0
        [Models.NTamd64.10.0]
        %Device%=Install,{id}
        """;

    private static MemoryStream Zip(string content, string privateName)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry(privateName).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }
        buffer.Position = 0;
        return buffer;
    }

    private static LocalOemDriverComparison.Report Inspect(
        string content, IReadOnlyList<DriverInfo> devices, int index)
    {
        // Source archive filenames are private, and must be absent from
        // every public finding and summary after this synthetic import.
        using var zip = Zip(content,
            $"PRIVATE_ORDER_1234_{index}/PRIVATE_INF_{index}.inf");
        return LocalOemDriverComparison.Compare(
            zip, devices, "x64", 10, 0, 26100);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FirmwareWithHigherNumberCannotHideOrdinaryNvidiaInfWhenMetadataUnknown(
        bool firmwareFirst)
    {
        var devices = new[] { Device(Nvidia, metadataVerified: false) };
        var firmware = Inspect(Inf(Nvidia, "Firmware",
            "11/05/2026", "999.0.0.1"), devices, 1);
        var graphics = Inspect(Inf(Nvidia, "Display",
            "11/05/2026", "33.0.0.1"), devices, 2);
        var reports = firmwareFirst
            ? new[] { firmware, graphics }
            : [graphics, firmware];
        var combined = LocalOemArchiveBatchSummary.Combine(reports);

        Assert.Equal(2, combined.TotalAssociations);
        Assert.Equal(1, combined.DistinctMatchedDevices);
        Assert.Equal(1, combined.RepeatedAssociations);
        Assert.Equal(0, combined.PotentialNewerShown);
        Assert.Equal(0, combined.FirmwareInfShown);
        Assert.Single(combined.Findings);
        var selected = combined.Findings[0];
        Assert.False(selected.IsFirmwareInf);
        Assert.Equal("33.0.0.1", selected.PackageVersion);
        Assert.Equal("non verificata", selected.InstalledVersion);
        Assert.False(selected.IsPotentialNewer);
        Assert.DoesNotContain("PRIVATE_ORDER", combined.Summary);
        Assert.DoesNotContain("PRIVATE_INF", selected.InfName);
        Assert.False(combined.IsInstallable);
        Assert.False(combined.IsVerifiedCompatible);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OldDatedHighNumberCannotHideNewerDatedNvidiaAcrossArchives(
        bool oldFirst)
    {
        var devices = new[] { Device(Nvidia) };
        var older = Inspect(Inf(Nvidia, "Display",
            "01/15/2022", "99.0.0.1"), devices, 1);
        var newer = Inspect(Inf(Nvidia, "Display",
            "11/05/2026", "33.0.0.1"), devices, 2);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.Older,
            Assert.Single(older.Findings).DriverVerDateRelation);
        Assert.False(Assert.Single(older.Findings).IsPotentialNewer);
        Assert.True(Assert.Single(newer.Findings).IsPotentialNewer);
        var batch = LocalOemArchiveBatchSummary.Combine(
            oldFirst ? [older, newer] : [newer, older]);

        Assert.Equal(1, batch.DistinctMatchedDevices);
        Assert.Equal(2, batch.TotalAssociations);
        Assert.Equal(1, batch.RepeatedAssociations);
        Assert.Equal(1, batch.PotentialNewerShown);
        var chosen = Assert.Single(batch.Findings);
        Assert.Equal("33.0.0.1", chosen.PackageVersion);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.Newer,
            chosen.DriverVerDateRelation);
        Assert.True(chosen.IsPotentialNewer);
        Assert.False(batch.IsInstallable);
    }

    [Fact]
    public void MultipleVendorsKeepDistinctDevicesAndExplicitFirmwareStatus()
    {
        var devices = new[]
        {
            Device(Nvidia), Device(Killer, installedVersion: "23.0.0.1"),
            Device(Realtek, installedVersion: "9.0.0.1")
        };
        // Two separate archives share the same NVIDIA PnP instance;
        // Killer remains visible and Realtek firmware is never a driver.
        var firmwareGpu = Inspect(Inf(Nvidia, "Firmware",
            "11/05/2026", "999.0.0.1"), devices, 1);
        var realtekFirmware = Inspect(Inf(Realtek, "Firmware",
            "11/05/2026", "99.0.0.1"), devices, 2);
        var gpuDriver = Inspect(Inf(Nvidia, "Display",
            "11/05/2026", "33.0.0.1"), devices, 3);
        var killerDriver = Inspect(Inf(Killer, "Net",
            "11/05/2026", "25.0.0.1"), devices, 4);

        var combined = LocalOemArchiveBatchSummary.Combine(
            [firmwareGpu, realtekFirmware, gpuDriver, killerDriver]);
        Assert.Equal(3, combined.DistinctMatchedDevices);
        Assert.Equal(4, combined.TotalAssociations);
        Assert.Equal(1, combined.RepeatedAssociations);
        Assert.Equal(3, combined.Findings.Count);
        Assert.Equal(2, combined.PotentialNewerShown);
        Assert.Equal(1, combined.FirmwareInfShown);
        Assert.Contains(combined.Findings, row => row.IsFirmwareInf &&
            !row.IsPotentialNewer);
        Assert.Contains(combined.Findings, row =>
            !row.IsFirmwareInf && row.PackageVersion == "33.0.0.1");
        Assert.Contains(combined.Findings, row =>
            !row.IsFirmwareInf && row.PackageVersion == "25.0.0.1");
        Assert.Contains("INF Firmware/BIOS fra le righe mostrate",
            combined.Summary);
        Assert.Contains(combined.Notes, line =>
            line.Contains("sola revisione") &&
            line.Contains("Firmware/BIOS"));
        Assert.All(combined.Findings, finding =>
            Assert.DoesNotContain("PRIVATE_ORDER", finding.InfName));
        Assert.False(combined.IsInstallable);
    }

    [Fact]
    public void ExactFirmwareHardwareIdStillBeatsWeakOrdinaryCompatibleId()
    {
        var devices = new[] { Device(Nvidia,
            compatible: NvidiaCompatible) };
        var firmwareExact = Inspect(Inf(Nvidia, "Firmware",
            "11/05/2026", "999.0.0.1"), devices, 1);
        var ordinaryCompatible = Inspect(Inf(NvidiaCompatible, "Display",
            "11/05/2026", "33.0.0.1"), devices, 2);

        var result = LocalOemArchiveBatchSummary.Combine(
            [ordinaryCompatible, firmwareExact]);
        var chosen = Assert.Single(result.Findings);
        Assert.True(chosen.IsFirmwareInf);
        Assert.True(chosen.HasExactHardwareId);
        Assert.Equal(0, result.PotentialNewerShown);
        Assert.Equal(1, result.FirmwareInfShown);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void LegacyBatchWithoutOrdinalsCannotFabricateDistinctDevices()
    {
        var firmware = new LocalOemDriverComparison.Finding(
            "OEM", "PRIVATE_ORDER.inf", "non verificata", "999.0.0.1",
            2, InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            LocalOemDriverComparison.VersionRelation.Unavailable)
        {
            IsFirmwareInf = true,
            InstalledDriverMetadataVerified = false
        };
        var legacy = new LocalOemDriverComparison.Report(
            1, 1, [firmware], 0, []);
        var batch = LocalOemArchiveBatchSummary.Combine([legacy]);
        Assert.Null(batch.DistinctMatchedDevices);
        Assert.Equal(0, batch.PotentialNewerShown);
        Assert.Equal(1, batch.FirmwareInfShown);
        Assert.Contains("NON dispositivi unici", batch.Summary);
        Assert.DoesNotContain("PRIVATE_ORDER", batch.Summary);
        Assert.DoesNotContain("PRIVATE_ORDER", Assert.Single(batch.Findings).InfName);
    }
}
