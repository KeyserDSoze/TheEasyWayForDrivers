using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class InfPlatformPreflightTests
{
    private static DriverInfo Device() =>
        new("PCI\\VEN_8086", "Peripheral", "Intel", "System", "Intel",
            "1.2.3.4", null, null, true, true, 0,
            [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
            [@"PCI\VEN_8086&DEV_1234"]);

    private static string Inf(string decoration = "NTamd64.10.0...22000",
        string hardwareId = @"PCI\VEN_8086&DEV_1234&SUBSYS_00000001",
        string catalog = "drivers.cat") =>
        $"""
        [Version]
        Signature="$Windows NT$"
        CatalogFile.NTamd64={catalog}
        DriverVer=04/15/2026,1.2.3.4
        [Manufacturer]
        %Vendor%=Models,{decoration}
        [Models.{decoration}]
        %Device%=Install,{hardwareId}
        """;

    [Fact]
    public void ValidArmOrAmd64MatchStillDoesNotClaimSignatureVerified()
    {
        var report = InfPlatformPreflight.Assess(Inf(), Device(), "x64", 10, 0, 22631, true);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch, report.Result);
        Assert.Equal(2, report.HardwareMatchStrength);
        Assert.Contains("NON verificata", report.CatalogStatus);
        Assert.Equal("drivers.cat", report.CatalogName);
        Assert.Contains("04/15/2026", report.DriverVersion);
    }

    [Fact]
    public void Arm64BuildDecorationsAreComparedIndependently()
    {
        var report = InfPlatformPreflight.Assess(
            Inf("NTarm64.10.0...26100"), Device(), "ARM64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch, report.Result);
    }

    [Fact]
    public void ArchitectureMismatchPreventsPositiveCompatibilitySignal()
    {
        var report = InfPlatformPreflight.Assess(Inf(), Device(), "arm64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ArchitectureMismatch, report.Result);
    }

    [Fact]
    public void OlderWindowsBuildIsFlagged()
    {
        var report = InfPlatformPreflight.Assess(
            Inf("NTamd64.10.0...26100"), Device(), "AMD64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.MinimumWindowsNotMet, report.Result);
    }

    [Fact]
    public void DifferentHardwareIdDoesNotPass()
    {
        var report = InfPlatformPreflight.Assess(
            Inf(hardwareId: @"PCI\VEN_8086&DEV_1235"), Device(),
            "AMD64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch, report.Result);
    }

    [Fact]
    public void OnlyCompatibleIdIsWeakerEvidence()
    {
        var report = InfPlatformPreflight.Assess(
            Inf(hardwareId: @"PCI\VEN_8086&DEV_1234"), Device(),
            "x64", 10, 0, 22631, false);
        Assert.Equal(1, report.HardwareMatchStrength);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch, report.Result);
    }

    [Fact]
    public void UnknownProductTypeRequiresManualReview()
    {
        var report = InfPlatformPreflight.Assess(
            Inf("NTamd64.10.0.3..22000"), Device(), "x64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired, report.Result);
    }

    [Fact]
    public void UndecoratedModelSectionRequiresManualReview()
    {
        var inf = """
            [Version]
            Signature="$Windows NT$"
            [Manufacturer]
            %Vendor%=Models
            [Models]
            %Driver%=Install, PCI\VEN_8086&DEV_1234
            """;
        var report = InfPlatformPreflight.Assess(inf, Device(), "x64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired, report.Result);
    }

    [Theory]
    [InlineData(@"..\drivers.cat")]
    [InlineData(@"\\server\drivers.cat")]
    [InlineData(@"C:\drivers.cat")]
    [InlineData("drivers.exe")]
    public void UnsafeCatalogNamesAreRejected(string catalog)
    {
        Assert.Null(InfPlatformPreflight.GetCatalogFileName(Inf(catalog: catalog), "amd64"));
    }

    [Fact]
    public void CatalogFilePresenceDoesNotEqualSignatureValidation()
    {
        var report = InfPlatformPreflight.Assess(Inf(), Device(), "x64", 10, 0, 22631, true);
        Assert.DoesNotContain("firma valida", report.CatalogStatus);
        Assert.Contains("NON verificata", report.CatalogStatus);
    }

    [Fact]
    public void MissingOrInvalidInfSignatureIsNotClassifiedAsMatch()
    {
        var report = InfPlatformPreflight.Assess(
            Inf().Replace("Signature=\"$Windows NT$\"", "Signature=\"NotAnInf\""),
            Device(), "x64", 10, 0, 22631, true);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired, report.Result);
    }

    [Fact]
    public void OversizedInfIsRejectedBeforeParsing()
    {
        Assert.Throws<InvalidDataException>(() => InfPlatformPreflight.Assess(
            new string('x', 1024 * 1024 + 1), Device(), "x64", 10, 0, 22631, false));
    }

    [Fact]
    public void NoSelectedDeviceKeepsInfUnverified()
    {
        var report = InfPlatformPreflight.Assess(Inf(), null, "x64", 10, 0, 22631, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ManualReviewRequired, report.Result);
    }
}
