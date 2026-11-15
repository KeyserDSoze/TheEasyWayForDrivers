using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #464: the preflight and ZIP OEM comparison must not promote a malformed
/// local HardwareId to exact evidence when an independently valid
/// CompatibleId admits the INF to the targeted read-only scan.
/// </summary>
public sealed class InfPreflightStrictPnpIdentityTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string Logitech = @"HID\VID_046D&PID_C534";

    private static DriverInfo Device(
        IReadOnlyList<string> hardware, IReadOnlyList<string>? compatible = null) =>
        new("PRIVATE_INSTANCE_SERIAL", "Generic Windows device",
            "Microsoft", "Display", "Microsoft", "31.0.0.0",
            null, "PRIVATE_OEM.inf", true, true, 0,
            hardware, compatible ?? []);

    private static string Inf(string primary, string? alias = null) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "Class=Display\nDriverVer=11/15/2026,32.0.0.0\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n%Device%=Install," +
        primary + (alias is null ? "" : "," + alias) + "\n";

    private static InfPlatformPreflight.Report Preflight(
        DriverInfo device, string inf) =>
        InfPlatformPreflight.Assess(inf, device, "AMD64",
            10, 0, 26100, catalogFileExists: false);

    private static LocalOemDriverComparison.Report CompareZip(
        DriverInfo device, string inf)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes,
                   ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(
                zip.CreateEntry("vendor/graphics.inf").Open(),
                new UTF8Encoding(false));
            writer.Write(inf);
        }
        bytes.Position = 0;
        return LocalOemDriverComparison.Compare(
            bytes, [device], "AMD64", 10, 0, 26100);
    }

    [Theory]
    [InlineData(Nvidia, "\t")]
    [InlineData(Nvidia, "\n")]
    [InlineData(Nvidia, "\r")]
    [InlineData(Nvidia, "\u00A0")]
    [InlineData(Nvidia, "\u202E")]
    [InlineData(Killer, "\t")]
    [InlineData(Realtek, "\n")]
    [InlineData(Logitech, "\t")]
    public void MalformedPrimaryPlusValidCompatibleIsNotExact(
        string id, string suffix)
    {
        var malformed = id + suffix;
        var device = Device([malformed], [id]);
        var inf = Inf(id);

        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(malformed));
        Assert.Single(InfModelEvidence.ReadMatchingIds(inf, [device]));
        Assert.Equal(1, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(inf)));

        var result = Preflight(device, inf);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            result.Result);
        Assert.Equal(1, result.HardwareMatchStrength);
        Assert.Contains("Compatible ID", result.Explanation);
        Assert.DoesNotContain("Hardware ID esatto", result.Explanation);
    }

    [Theory]
    [InlineData(Nvidia)]
    [InlineData(Killer)]
    [InlineData(Realtek)]
    [InlineData(Logitech)]
    public void MalformedOnlyHardwareCannotClaimAnInfMatch(string id)
    {
        var device = Device([id + "\t"]);
        var report = Preflight(device, Inf(id));

        Assert.Equal(InfPlatformPreflight.Verdict.NoHardwareMatch,
            report.Result);
        Assert.Equal(0, report.HardwareMatchStrength);
        Assert.Empty(InfModelEvidence.ReadMatchingIds(Inf(id), [device]));
    }

    [Theory]
    [InlineData(Nvidia)]
    [InlineData(Killer)]
    [InlineData(Realtek)]
    [InlineData(Logitech)]
    public void StrictValidHardwareStillClaimsExactStrength(string id)
    {
        var device = Device(["  " + id.ToLowerInvariant() + "  "],
            [id + "\t"]);
        var inf = Inf(id);

        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(
            device.HardwareIds[0]));
        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(inf)));
        Assert.Equal(2, Preflight(device, inf).HardwareMatchStrength);
    }

    [Fact]
    public void SecondaryInfAliasCannotBeUpgradedToPrimaryByValidDevice()
    {
        var inf = Inf(@"PCI\VEN_9999&DEV_0001", Nvidia);
        var device = Device([Nvidia]);

        var declared = InfModelEvidence.ReadMatchingIds(inf, [device]);
        Assert.False(Assert.Single(declared).IsPrimaryHardwareId);
        Assert.Equal(1, InfModelEvidence.MatchStrength(device, declared));
        var preflight = Preflight(device, inf);
        Assert.Equal(1, preflight.HardwareMatchStrength);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            preflight.Result);
    }

    [Fact]
    public void MixedInvalidPrimaryAndValidCompatibleDoNotInventNewerZipUpdate()
    {
        // A genuine higher DriverVer plus a valid CompatibleId still
        // cannot satisfy the exact-primary requirement for "newer".
        var compatible = Device([Nvidia + "\t"], [Nvidia]);
        var report = CompareZip(compatible, Inf(Nvidia));

        Assert.Equal(1, report.InfExamined);
        Assert.Equal(1, report.TotalMatchedDevices);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(1, finding.HardwareMatchStrength);
        Assert.False(finding.HasExactHardwareId);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            finding.PlatformVerdict);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
            finding.VersionComparison);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
        Assert.DoesNotContain("PRIVATE_OEM", report.Summary);

        // The SAME OEM INF legitimately yields a higher-version hint
        // when Windows supplies a strictly validated PRIMARY ID.
        var exact = CompareZip(Device([Nvidia]), Inf(Nvidia));
        var exactFinding = Assert.Single(exact.Findings);
        Assert.Equal(2, exactFinding.HardwareMatchStrength);
        Assert.True(exactFinding.IsPotentialNewer);
        Assert.Equal(1, exact.PotentialNewerDevices);
        Assert.False(exact.IsInstallable);
    }

    [Fact]
    public void NoStrictLocalIdNeverGetsZipOemEvidence()
    {
        var report = CompareZip(
            Device([Realtek + "\n"], [Realtek + "\t"]), Inf(Realtek));
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
    }
}
