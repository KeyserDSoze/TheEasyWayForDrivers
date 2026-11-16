using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #501: a PnP ID appearing only in NTarm64 Models does not consume
/// an x64 CAT check, despite being found by the full 4096-ID scanner.
/// Architecture-only checks are NOT full OS/SetupAPI compatibility.
/// </summary>
public sealed class OemFolderCatalogArchitectureBoundaryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private sealed class Folder : IDisposable
    {
        public string Root { get; } =
            Directory.CreateTempSubdirectory("OmegaDrive-CatArch-501-").FullName;
        public void Add(string relative, string contents)
        {
            var path = Path.Combine(Root,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static DriverInfo Device(string id) =>
        new(id + @"\SENSITIVE_PNP_INSTANCE", "Generic device",
            "Microsoft", "System", "Microsoft", "1.0.0.0",
            null, "oem123.inf", true, true, 0, [id], []);

    private static string Inf(string manufacturer, string sections) =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        CatalogFile=verified.cat
        DriverVer=10/20/2026,33.0.0.1
        [Manufacturer]
        Vendor={manufacturer}
        {sections}
        """;

    private static string ModelSection(string architecture, params string[] ids) =>
        $"[Models.{architecture}]\n" + string.Join("\n",
            ids.Select((id, i) => $"Device{i}=Install,{id}"));

    private static OemFolderCatalogAuditPlan.Plan Plan(
        Folder folder, string architecture, params DriverInfo[] devices) =>
        OemFolderCatalogAuditPlan.Create(folder.Root, devices, architecture);

    [Theory]
    [InlineData("Models.NTarm64.10.0", "x64", true)]
    [InlineData("Models.NTamd64.10.0", "arm64", true)]
    [InlineData("Models.NTx86.10.0", "x64", true)]
    [InlineData("Models.NTamd64.10.0", "x86", true)]
    [InlineData("Models.NTamd64.10.0...26100", "amd64", false)]
    [InlineData("Models.NT", "x64", false)]
    [InlineData("Models", "x64", false)]
    [InlineData("Models.NTfuture", "x64", false)]
    [InlineData("Models.NTarm64", "unidentified", false)]
    public void SharedSectionParserExcludesOnlyExplicitArchitectureMismatch(
        string section, string architecture, bool mismatched)
    {
        Assert.Equal(mismatched,
            InfPlatformPreflight.IsExplicitArchitectureMismatch(
                section, architecture));
    }

    [Theory]
    [InlineData("NTarm64.10.0", "x64")]
    [InlineData("NTamd64.10.0", "arm64")]
    [InlineData("NTx86.10.0", "amd64")]
    public void WrongArchitectureOnlyCannotBecomeVerifiedCatInfCheck(
        string target, string actual)
    {
        using var folder = new Folder();
        folder.Add("firmware/cross.inf", Inf($"Models,{target}",
            ModelSection(target, Nvidia)));
        folder.Add("firmware/verified.cat", "UNSIGNED TEST CAT");

        var plan = Plan(folder, actual, Device(Nvidia));
        Assert.Equal(1, plan.InfExamined);
        Assert.Equal(0, plan.MatchedInf);
        Assert.Equal(1, plan.ArchitectureMismatchInf);
        Assert.Equal(0, plan.MissingCatalog);
        Assert.Equal(0, plan.DeferredInf);
        Assert.Empty(plan.Checks);
        Assert.False(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);

        var report = OemFolderCatalogAuditSummary.Create(plan, []);
        Assert.Equal(1, report.ArchitectureMismatchInf);
        Assert.Equal(0, report.VerifiedInfMembers);
        Assert.Contains("altra architettura Windows", report.Text);
        Assert.DoesNotContain("firmware/cross.inf", report.Text);
        Assert.DoesNotContain("SENSITIVE_PNP_INSTANCE", report.Text);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void SixteenArmOnlyInfsCannotStealSignatureSlotsFromX64Drivers()
    {
        using var folder = new Folder();
        folder.Add("arm/verified.cat", "UNSIGNED TEST");
        for (var i = 0; i < 16; i++)
            folder.Add($"arm/arm{i:D2}.inf",
                Inf("Models,NTarm64.10.0",
                    ModelSection("NTarm64.10.0", Nvidia)));

        folder.Add("gpu/verified.cat", "UNSIGNED TEST");
        folder.Add("gpu/display.inf", Inf("Models,NTamd64.10.0",
            ModelSection("NTamd64.10.0", Nvidia)));
        folder.Add("wifi/verified.cat", "UNSIGNED TEST");
        folder.Add("wifi/killer.inf", Inf("Models,NTamd64.10.0",
            ModelSection("NTamd64.10.0", Killer)));
        folder.Add("audio/verified.cat", "UNSIGNED TEST");
        folder.Add("audio/realtek.inf", Inf("Models,NTamd64.10.0",
            ModelSection("NTamd64.10.0", Realtek)));

        var plan = Plan(folder, "x64",
            Device(Nvidia), Device(Killer), Device(Realtek));

        Assert.Equal(19, plan.InfExamined);
        Assert.Equal(16, plan.ArchitectureMismatchInf);
        Assert.Equal(3, plan.MatchedInf);
        Assert.Equal(3, plan.Checks.Count);
        Assert.Equal(0, plan.DeferredInf);
        Assert.Equal(0, plan.FirmwareInfChecks);
        Assert.Contains(plan.Checks,
            x => Path.GetFileName(x.InfPath) == "display.inf");
        Assert.Contains(plan.Checks,
            x => Path.GetFileName(x.InfPath) == "killer.inf");
        Assert.Contains(plan.Checks,
            x => Path.GetFileName(x.InfPath) == "realtek.inf");
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void MixedArchitectureInfGetsSlotOnlyForItsMatchingTargetIds()
    {
        using var folder = new Folder();
        folder.Add("mixed/verified.cat", "UNSIGNED TEST");
        var mixed = Inf("Models,NTarm64.10.0,NTamd64.10.0",
            ModelSection("NTarm64.10.0", Nvidia) + "\n" +
            ModelSection("NTamd64.10.0", Killer));
        folder.Add("mixed/multiplatform.inf", mixed);
        folder.Add("normal/verified.cat", "UNSIGNED TEST");
        // This is a stronger two-device x64 candidate than mixed,
        // which has just ONE x64 PnP association (Killer).
        folder.Add("normal/gpu-audio.inf", Inf("Models,NTamd64.10.0",
            ModelSection("NTamd64.10.0", Nvidia, Realtek)));

        var plan = Plan(folder, "x64",
            Device(Nvidia), Device(Killer), Device(Realtek));
        Assert.Equal(2, plan.MatchedInf);
        Assert.Equal(0, plan.ArchitectureMismatchInf);
        Assert.Equal("gpu-audio.inf",
            Path.GetFileName(plan.Checks[0].InfPath));
        Assert.Equal("multiplatform.inf",
            Path.GetFileName(plan.Checks[1].InfPath));
        Assert.Equal(0, plan.DeferredInf);
    }

    [Theory]
    [InlineData("NT", "x64")]
    [InlineData("", "x64")]
    [InlineData("NTarm64.10.0", "unknown-arch")]
    public void UndecoratedOrUnknownPlatformIsNotMistakenForExplicitMismatch(
        string decoration, string architecture)
    {
        using var folder = new Folder();
        folder.Add("driver/verified.cat", "UNSIGNED TEST");
        var section = decoration.Length == 0 ? "Models" : "Models." + decoration;
        folder.Add("driver/driver.inf", Inf(decoration.Length == 0
                ? "Models" : "Models," + decoration,
            $"[{section}]\nDevice=Install,{Killer}"));
        var plan = Plan(folder, architecture, Device(Killer));
        Assert.Equal(0, plan.ArchitectureMismatchInf);
        Assert.Equal(1, plan.MatchedInf);
        Assert.Single(plan.Checks);
        Assert.False(plan.IsFullPackageVerified);
    }

    [Fact]
    public void ArchitectureMismatchDoesNotConcealIndependentMissingCatalog()
    {
        using var folder = new Folder();
        folder.Add("wrong/driver.inf", Inf("Models,NTarm64",
            ModelSection("NTarm64", Nvidia)));
        folder.Add("native/driver.inf", Inf("Models,NTamd64",
            ModelSection("NTamd64", Killer)));
        var plan = Plan(folder, "x64", Device(Nvidia), Device(Killer));
        Assert.Equal(1, plan.ArchitectureMismatchInf);
        Assert.Equal(1, plan.MatchedInf);
        Assert.Equal(1, plan.MissingCatalog);
        Assert.Empty(plan.Checks);
        Assert.False(plan.IsComplete);
    }
}
