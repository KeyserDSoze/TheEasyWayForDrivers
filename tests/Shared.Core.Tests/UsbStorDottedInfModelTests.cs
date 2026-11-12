using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// A dotted USBSTOR revision is a valid, *specific* Windows PnP model.
/// It must survive the INF allowlist before OEM archive/folder comparison,
/// without relaxing any other enumerator or granting installation.
/// </summary>
public sealed class UsbStorDottedInfModelTests
{
    private const string Drive =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_1.00";
    private const string OtherDrive =
        @"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer_Blade&Rev_2.00";

    private static DriverInfo Device(string id = Drive,
        IReadOnlyList<string>? compatibleIds = null) =>
        new("PRIVATE_DISK_INSTANCE_SERIAL",
            "PRIVATE Storage Model", "Microsoft", "DiskDrive",
            "Microsoft", "1.0", null, "PRIVATE_oem42.inf",
            true, true, 0, [id], compatibleIds ?? []);

    private static string Inf(string primary, string? additional = null) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "CatalogFile.NTamd64=drivers.cat\n" +
        "DriverVer=11/02/2026,1.2.3.4\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n" +
        "%Drive%=Install, " + primary +
        (additional is null ? "" : ", " + additional);

    [Theory]
    [InlineData(Drive)]
    [InlineData(@"USBSTOR\Disk&Ven_ACME.CO&Prod_SSD-Drive_Pro&Rev_2.10")]
    [InlineData(@"USBSTOR\CdRom&Ven_SanDisk&Prod_USB.DVD&Rev_3.01")]
    public void ValidDottedUsbStorProductsAppearInInfPreviewAndTargetedMatch(
        string id)
    {
        Assert.True(UsbStorPnpMatchPolicy.IsSpecificHardwareId(id));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        var inf = Inf(id);
        var preview = Assert.Single(InfModelEvidence.ReadDeclaredIds(inf));
        Assert.Equal(id.ToUpperInvariant(), preview.HardwareId);
        Assert.True(preview.IsPrimaryHardwareId);

        var targeted = Assert.Single(
            InfModelEvidence.ReadMatchingIds(inf, [Device(id)]));
        Assert.Equal(id.ToUpperInvariant(), targeted.HardwareId);
        Assert.Equal(2, InfModelEvidence.MatchStrength(Device(id), [targeted]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device(OtherDrive), [targeted]));
    }

    [Fact]
    public void SpecificDottedInfModelPassesOnlyReadOnlyPlatformPreflight()
    {
        var result = InfPlatformPreflight.Assess(
            Inf(Drive), Device(), "x64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            result.Result);
        Assert.Equal(2, result.HardwareMatchStrength);
        Assert.Contains("NON verificata", result.CatalogStatus);
        Assert.Contains("NON prova", result.Explanation);
        Assert.Equal("drivers.cat", result.CatalogName);
        Assert.Equal("11/02/2026,1.2.3.4", result.DriverVersion);

        var incompatible = InfPlatformPreflight.Assess(
            Inf(Drive), Device(OtherDrive), "x64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            incompatible.Result);
        Assert.Equal(0, incompatible.HardwareMatchStrength);

        var wrongArchitecture = InfPlatformPreflight.Assess(
            Inf(Drive), Device(), "arm64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ArchitectureMismatch,
            wrongArchitecture.Result);
    }

    [Fact]
    public void DottedIdInInfCompatiblePositionNeverGetsExactHardwareStrength()
    {
        const string otherModel =
            @"USBSTOR\Disk&Ven_SanDisk&Prod_Different_Model&Rev_1.00";
        var entries = InfModelEvidence.ReadDeclaredIds(
            Inf(otherModel, Drive));
        Assert.Equal(2, entries.Count);
        var alias = Assert.Single(entries.Where(entry =>
            entry.HardwareId == Drive.ToUpperInvariant()));
        Assert.False(alias.IsPrimaryHardwareId);
        Assert.Equal(1, InfModelEvidence.MatchStrength(Device(), entries));

        var review = InfPlatformPreflight.Assess(
            Inf(otherModel, Drive), Device(), "x64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            review.Result);
        Assert.Equal(1, review.HardwareMatchStrength);
    }

    [Theory]
    [InlineData(@"USBSTOR\GenDisk.1")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_&Rev_1.00")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1..0")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1.00&FAKE_01")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1.00\PRIVATE")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1.00/evil")]
    [InlineData(@"USBSTOR\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1.00?inj")]
    [InlineData(@"SCSI\Disk&Ven_SanDisk&Prod_Cruzer&Rev_1.00")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0.1")]
    public void DottedIdsOutsideStrictUsbStorGrammarRemainUnrecognized(
        string raw)
    {
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(Inf(raw)));
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(raw),
            [Device(raw)]));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device(raw), InfModelEvidence.ReadDeclaredIds(Inf(raw))));
    }

    [Fact]
    public void AnonymousCoverageNeverExportsDottedModelOrLocalPath()
    {
        var report = ScanCoverageText.Create(null, [],
            1, 0, 0, [Device()], inventoryComplete: true);
        Assert.DoesNotContain(Drive, report);
        Assert.DoesNotContain("PRIVATE", report);
        Assert.DoesNotContain("Rev_1.00", report);
    }
}
