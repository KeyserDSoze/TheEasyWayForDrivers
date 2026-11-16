using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #497: Firmware setup-class INF packages are not ordinary driver updates,
/// even if their PnP Hardware ID and numeric DriverVer appear newer.
/// </summary>
public sealed class LocalOemFirmwareInfBoundaryTests
{
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string FirmwareClassId =
        "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}";

    private static DriverInfo Device(string id) =>
        new(id + @"\SYNTHETIC_INSTANCE", "Microsoft Basic Device",
            "Microsoft", "System", "Microsoft", "31.0.0.1",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "oem42.inf", true, true, 0, [id], []);

    private static string Inf(string id, string classFields,
        string version = "99.0.0.1") => $"""
        [Version]
        Signature="$Windows NT$"
        {classFields}
        DriverVer=11/05/2026,{version}
        [Manufacturer]
        %Vendor%=Models,NTamd64.10.0
        [Models.NTamd64.10.0]
        %Device%=Install,{id}
        """;

    private static MemoryStream Archive(
        params (string Filename, string Inf)[] entries)
    {
        var data = new MemoryStream();
        using (var zip = new ZipArchive(data, ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var (name, inf) in entries)
            {
                using var writer = new StreamWriter(
                    zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(inf);
            }
        }
        data.Position = 0;
        return data;
    }

    [Theory]
    [InlineData("Class=Firmware")]
    [InlineData("Class=BIOS")]
    [InlineData("ClassGuid={f2e7dd72-6468-4e36-b6f1-6488f42c1b52}")]
    [InlineData("Class=Display\nClassGuid={F2E7DD72-6468-4E36-B6F1-6488F42C1B52}")]
    [InlineData("Class=Display\nClass=Firmware")]
    [InlineData("Class=Firmware\nClass=Display")]
    public void ExplicitFirmwareInfIsInformationalEvenWithNewerDriverVer(
        string classFields)
    {
        var inf = Inf(Gpu, classFields);
        Assert.True(InfPlatformPreflight.IsFirmwareUpdateClass(inf));
        using var archive = Archive(("firmware.inf", inf));

        var report = LocalOemDriverComparison.Compare(
            archive, [Device(Gpu)], "x64", 10, 0, 26100);
        var finding = Assert.Single(report.Findings);
        Assert.True(finding.HasExactHardwareId);
        Assert.True(finding.IsFirmwareInf);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            finding.VersionComparison);
        Assert.Equal(LocalOemDriverComparison.InfDateRelation.NotVerified,
            finding.DriverVerDateRelation);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Contains("INF Firmware/BIOS", finding.Summary);
        Assert.Contains("NON è", finding.Summary);
        Assert.DoesNotContain("SYNTHETIC_INSTANCE", report.Summary);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Theory]
    [InlineData("Class=Display")]
    [InlineData("Class=Net")]
    [InlineData("Class=System")]
    [InlineData("Class=SoftwareComponent")]
    [InlineData("Class=Display\nClassGuid={4d36e968-e325-11ce-bfc1-08002be10318}")]
    public void OrdinaryDeviceInfIsNotMisclassifiedAsFirmware(
        string classFields)
    {
        var inf = Inf(Gpu, classFields, "33.0.0.1");
        Assert.False(InfPlatformPreflight.IsFirmwareUpdateClass(inf));
        using var archive = Archive(("gpu.inf", inf));
        var finding = Assert.Single(LocalOemDriverComparison.Compare(
            archive, [Device(Gpu)], "x64", 10, 0, 26100).Findings);
        Assert.False(finding.IsFirmwareInf);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            finding.VersionComparison);
        Assert.True(finding.IsPotentialNewer);
    }

    [Fact]
    public void FirmwareGuidAloneCannotBeHiddenByOrdinaryClassDirective()
    {
        var text = Inf(Gpu,
            "Class=Display\nClassGuid=" + FirmwareClassId);
        Assert.True(InfPlatformPreflight.IsFirmwareUpdateClass(text));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryGpuDriverWinsOverFirmwareWithSamePnpId(
        bool firmwareFirst)
    {
        var firmware = ("firmware.inf", Inf(Gpu,
            "Class=Firmware", "999.0.0.1"));
        var display = ("graphics.inf", Inf(Gpu,
            "Class=Display", "33.0.0.1"));
        using var archive = firmwareFirst
            ? Archive(firmware, display) : Archive(display, firmware);
        var report = LocalOemDriverComparison.Compare(
            archive, [Device(Gpu)], "x64", 10, 0, 26100);

        var selected = Assert.Single(report.Findings);
        Assert.False(selected.IsFirmwareInf);
        Assert.Equal("33.0.0.1", selected.PackageVersion);
        Assert.True(selected.IsPotentialNewer);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Equal(1, report.PotentialNewerDevices);
        Assert.Equal(1, report.AdditionalMatchesOmitted);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void MixedGpuKillerRealtekBundleDoesNotCountFirmwareAsOrdinary()
    {
        using var archive = Archive(
            ("nvidia.inf", Inf(Gpu, "Class=Firmware")),
            ("killer.inf", Inf(Killer, "Class=Net", "25.0.0.1")),
            ("realtek.inf", Inf(Realtek, "Class=MEDIA", "35.0.0.1")));
        var report = LocalOemDriverComparison.Compare(
            archive, [Device(Gpu), Device(Killer), Device(Realtek)],
            "x64", 10, 0, 26100);

        Assert.Equal(3, report.TotalMatchedDevices);
        Assert.Equal(2, report.PotentialNewerDevices);
        Assert.Contains(report.Findings, f => f.IsFirmwareInf &&
            f.HasExactHardwareId && !f.IsPotentialNewer);
        Assert.Contains(report.Findings, f => f.PackageVersion == "25.0.0.1" &&
            f.IsPotentialNewer);
        Assert.Contains(report.Findings, f => f.PackageVersion == "35.0.0.1" &&
            f.IsPotentialNewer);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void FolderComparisonUsesSameFirmwareVetoAndKeepsPnpEvidence()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "omega-firmware-497-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "firmware.inf"),
                Inf(Gpu, "ClassGuid=" + FirmwareClassId),
                new UTF8Encoding(false));
            var result = LocalOemFolderComparison.Compare(
                folder, [Device(Gpu)], "x64", 10, 0, 26100);
            var row = Assert.Single(result.Findings);
            Assert.True(row.IsFirmwareInf);
            Assert.True(row.HasExactHardwareId);
            Assert.Equal(0, result.PotentialNewerDevices);
            Assert.False(result.IsInstallable);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
