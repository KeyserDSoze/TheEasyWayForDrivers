using System.Text;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #500: scarce opt-in CAT/INF checks must favor ordinary drivers,
/// while keeping firmware-only devices as read-only review evidence.
/// </summary>
public sealed class OemFolderCatalogFirmwareFairnessTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
    private const string FirmwareGuid =
        "{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}";

    private sealed class Folder : IDisposable
    {
        public string Root { get; } =
            Directory.CreateTempSubdirectory("OmegaDrive-Firmware-CAT-500-").FullName;
        public void Add(string relative, string text)
        {
            var name = Path.Combine(Root, relative.Replace('/',
                Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(name)!);
            File.WriteAllText(name, text, new UTF8Encoding(false));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static DriverInfo Device(string hardwareId) =>
        new(hardwareId + @"\PRIVATE_INSTANCE_500", "Windows device",
            "Microsoft", "System", "Microsoft", "1.0.0.0",
            null, "oem20.inf", true, true, 0, [hardwareId], []);

    private static string Inf(string classFields, params string[] ids)
    {
        var models = string.Join("\n", ids.Select((id, n) =>
            $"Device{n}=Install,{id}"));
        return $"""
            [Version]
            Signature="$Windows NT$"
            {classFields}
            CatalogFile.NTamd64=verified.cat
            DriverVer=11/16/2026,10.0.0.0
            [Manufacturer]
            Vendor=Models,NTamd64.10.0
            [Models.NTamd64.10.0]
            {models}
            """;
    }

    private static OemFolderCatalogAuditPlan.Plan Plan(
        Folder folder, params DriverInfo[] devices) =>
        OemFolderCatalogAuditPlan.Create(folder.Root, devices, "x64");

    private static string Filename(OemFolderCatalogAuditPlan.Check check) =>
        Path.GetFileName(check.InfPath);

    [Fact]
    public void FifteenFirmwareCopiesCannotStealTwelveCatChecksFromGpuKillerRealtek()
    {
        using var folder = new Folder();
        folder.Add("firmware/verified.cat", "SYNTHETIC UNSIGNED CATALOG");
        for (var i = 0; i < 15; i++)
            folder.Add($"firmware/firmware{i:D2}.inf",
                Inf("Class=Firmware", Nvidia));

        folder.Add("gpu/verified.cat", "SYNTHETIC UNSIGNED CATALOG");
        folder.Add("gpu/display.inf", Inf("Class=Display", Nvidia));
        folder.Add("killer/verified.cat", "SYNTHETIC UNSIGNED CATALOG");
        folder.Add("killer/network.inf", Inf("Class=Net", Killer));
        folder.Add("realtek/verified.cat", "SYNTHETIC UNSIGNED CATALOG");
        folder.Add("realtek/audio.inf", Inf("Class=MEDIA", Realtek));

        var plan = Plan(folder, Device(Nvidia), Device(Killer), Device(Realtek));
        Assert.Equal(18, plan.InfExamined);
        Assert.Equal(18, plan.MatchedInf);
        Assert.Equal(12, plan.Checks.Count);
        Assert.Equal(6, plan.DeferredInf);
        Assert.Equal(9, plan.FirmwareInfChecks);
        Assert.Equal(6, plan.FirmwareInfDeferred);
        Assert.Equal(0, plan.MissingCatalog);
        Assert.Equal(0, plan.UnreadableInf);
        Assert.Equal(
            new[] { "audio.inf", "display.inf", "network.inf" }.Order(),
            plan.Checks.Take(3).Select(Filename).Order());
        Assert.All(plan.Checks.Skip(3), check =>
            Assert.StartsWith("firmware", Filename(check)));
        Assert.False(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);

        var row = new OemFolderCatalogAuditSummary.Row(
            CatalogTrustReport.Status.NotVerified,
            CatalogMemberVerification.Verdict.NotPerformed);
        var summary = OemFolderCatalogAuditSummary.Create(
            plan, Enumerable.Repeat(row, plan.Checks.Count));
        Assert.Equal(9, summary.FirmwareInfChecks);
        Assert.Equal(6, summary.FirmwareInfDeferred);
        Assert.Equal(0, summary.VerifiedInfMembers);
        Assert.Contains("Firmware/BIOS solo revisione", summary.Text);
        Assert.DoesNotContain(folder.Root, summary.Text);
        Assert.DoesNotContain("PRIVATE_INSTANCE", summary.Text);
        Assert.False(summary.IsInstallable);
    }

    [Fact]
    public void FirmwareOnlyDeviceStillGetsOptInSignatureCheckAsReview()
    {
        using var folder = new Folder();
        folder.Add("fw/verified.cat", "UNSIGNED TEST");
        folder.Add("fw/system.inf", Inf("ClassGuid=" + FirmwareGuid, Killer));

        var plan = Plan(folder, Device(Killer));
        Assert.Equal(1, plan.MatchedInf);
        Assert.Single(plan.Checks);
        Assert.Equal(1, plan.FirmwareInfChecks);
        Assert.Equal(0, plan.FirmwareInfDeferred);
        Assert.False(plan.IsFullPackageVerified);
        Assert.True(plan.IsComplete);

        var summary = OemFolderCatalogAuditSummary.Create(plan,
        [
            new OemFolderCatalogAuditSummary.Row(
                CatalogTrustReport.Status.NotVerified,
                CatalogMemberVerification.Verdict.NotPerformed)
        ]);
        Assert.Contains("1 controlli Firmware/BIOS", summary.Text);
        Assert.False(summary.IsInstallable);
    }

    [Fact]
    public void OrdinaryDriversWinOverFirmwareEvenWhenFirmwareMatchesMoreDevices()
    {
        using var folder = new Folder();
        folder.Add("fw/verified.cat", "UNSIGNED TEST");
        folder.Add("fw/multi.inf", Inf("Class=Display\nClassGuid=" + FirmwareGuid,
            Nvidia, Killer, Realtek));
        folder.Add("gpu/verified.cat", "UNSIGNED TEST");
        folder.Add("gpu/display.inf", Inf("Class=Display", Nvidia));

        var plan = Plan(folder, Device(Nvidia), Device(Killer), Device(Realtek));
        Assert.Equal(2, plan.MatchedInf);
        Assert.Equal(2, plan.Checks.Count);
        Assert.Equal("display.inf", Filename(plan.Checks[0]));
        Assert.Equal("multi.inf", Filename(plan.Checks[1]));
        Assert.Equal(1, plan.FirmwareInfChecks);
        Assert.Equal(0, plan.FirmwareInfDeferred);
    }

    [Fact]
    public void NewFirmwareDeviceOutranksRedundantOrdinaryDriverAndRemainsReviewOnly()
    {
        using var folder = new Folder();
        folder.Add("gpu/verified.cat", "UNSIGNED TEST");
        for (var i = 0; i < 15; i++)
            folder.Add($"gpu/display{i:D2}.inf",
                Inf("Class=Display", Nvidia));
        folder.Add("fw/verified.cat", "UNSIGNED TEST");
        folder.Add("fw/wireless.inf", Inf("Class=BIOS", Killer));

        var plan = Plan(folder, Device(Nvidia), Device(Killer));
        Assert.Equal(16, plan.MatchedInf);
        Assert.Equal(OemFolderCatalogAuditPlan.MaxChecks, plan.Checks.Count);
        Assert.Equal("wireless.inf", Filename(plan.Checks[1]));
        Assert.Equal(1, plan.FirmwareInfChecks);
        Assert.Equal(0, plan.FirmwareInfDeferred);
        Assert.Equal(4, plan.DeferredInf);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void ThirteenDistinctNormalDevicesTakeAllTwelveSlotsBeforeFirmware()
    {
        using var folder = new Folder();
        folder.Add("normal/verified.cat", "UNSIGNED TEST");
        var normalIds = Enumerable.Range(1, 13).Select(
            i => $@"PCI\VEN_8086&DEV_{i:X4}").ToArray();
        for (var i = 0; i < normalIds.Length; i++)
            folder.Add($"normal/driver{i:D2}.inf",
                Inf("Class=Net", normalIds[i]));

        folder.Add("fw/verified.cat", "UNSIGNED TEST");
        folder.Add("fw/firmware.inf", Inf("Class=Firmware", Nvidia));
        folder.Add("fw/bios.inf", Inf("Class=BIOS", Killer));

        var devices = normalIds.Concat([Nvidia, Killer])
            .Select(Device).ToArray();
        var plan = Plan(folder, devices);
        Assert.Equal(15, plan.MatchedInf);
        Assert.Equal(12, plan.Checks.Count);
        Assert.All(plan.Checks, item =>
            Assert.StartsWith("driver", Filename(item)));
        Assert.Equal(0, plan.FirmwareInfChecks);
        Assert.Equal(2, plan.FirmwareInfDeferred);
        Assert.Equal(3, plan.DeferredInf);
    }

    [Fact]
    public void LegacySyntheticPlanStillReportsNoFirmwareWithoutInference()
    {
        var plan = new OemFolderCatalogAuditPlan.Plan(
            1, 1, 0, 0, 0,
            [new OemFolderCatalogAuditPlan.Check(
                "PRIVATE_INF_500.inf", "PRIVATE_CAT_500.cat")]);
        var summary = OemFolderCatalogAuditSummary.Create(plan,
        [
            new OemFolderCatalogAuditSummary.Row(
                CatalogTrustReport.Status.NotVerified,
                CatalogMemberVerification.Verdict.NotPerformed)
        ]);
        Assert.Equal(0, summary.FirmwareInfChecks);
        Assert.Equal(0, summary.FirmwareInfDeferred);
        Assert.DoesNotContain("Firmware/BIOS solo revisione", summary.Text);
        Assert.DoesNotContain("PRIVATE_", summary.Text);
        Assert.False(summary.IsFullPackageVerified);
        Assert.False(summary.IsInstallable);
    }
}
