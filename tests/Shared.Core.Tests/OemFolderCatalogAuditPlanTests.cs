using System.Text;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemFolderCatalogAuditPlanTests
{
    private const string GpuId = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string AudioId = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B";
    private const string KillerId = @"PCI\VEN_8086&DEV_51F1";

    private sealed class TemporaryFolder : IDisposable
    {
        public string Root { get; } =
            Directory.CreateTempSubdirectory("OmegaDrive-CatPlanTest-").FullName;
        public string Add(string relative, string content)
        {
            var path = Path.Combine(Root,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static string Inf(string id, bool withCat = true) =>
        $"""
        [Version]
        Signature="$Windows NT$"
        Class=Display
        {(withCat ? "CatalogFile.NTamd64=verified.cat" : "")}
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64.10.0...22000
        [Models.NTamd64.10.0...22000]
        Device=Install,{id}
        """;

    private static DriverInfo Device(string hardware) =>
        new("PRIVATE_INSTANCE_ID_" + hardware, "Windows device",
            "OEM", "Display", "OEM", "31.0.15.5222",
            null, "oem1.inf", true, true, 0, [hardware], []);

    private static OemFolderCatalogAuditPlan.Plan Plan(
        TemporaryFolder folder, params DriverInfo[] devices) =>
        OemFolderCatalogAuditPlan.Create(folder.Root,
            devices, "AMD64");

    [Fact]
    public void OnlyRelatedInfAndSameDirectoryCatalogArePlanned()
    {
        using var folder = new TemporaryFolder();
        folder.Add("gpu/driver.inf", Inf(GpuId));
        folder.Add("gpu/verified.cat", "NOT ACTUALLY SIGNED");
        folder.Add("wifi/driver.inf", Inf(KillerId));
        folder.Add("wifi/verified.cat", "NOT ACTUALLY SIGNED");
        folder.Add("unrelated/driver.inf", Inf(AudioId));

        var plan = Plan(folder, Device(GpuId), Device(KillerId));
        Assert.Equal(3, plan.InfExamined);
        Assert.Equal(2, plan.MatchedInf);
        Assert.Equal(0, plan.MissingCatalog);
        Assert.Equal(0, plan.UnreadableInf);
        Assert.Equal(2, plan.Checks.Count);
        Assert.True(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);
        Assert.All(plan.Checks, check =>
        {
            Assert.Equal("verified.cat", Path.GetFileName(check.CatalogPath));
            Assert.Equal(Path.GetDirectoryName(check.InfPath),
                Path.GetDirectoryName(check.CatalogPath));
        });
    }

    [Fact]
    public void AuditsNvidiaBeyondFiveHundredPreviewIdsAlongsideKillerAndAudio()
    {
        using var folder = new TemporaryFolder();
        // A multi-model NVIDIA/OEM INF can declare hundreds of unrelated
        // identifiers before the PnP hardware actually present on this PC.
        var unrelated = string.Join("\n", Enumerable.Range(0, 540)
            .Select(i => $"Other{i}=Install,PCI\\VEN_10DE&DEV_{i:X4}"));
        var largeInf = Inf(GpuId).Replace(
            $"Device=Install,{GpuId}",
            unrelated + $"\nDevice=Install,{GpuId}");
        Assert.Equal(500, InfModelEvidence.ReadDeclaredIds(largeInf).Count);
        Assert.DoesNotContain(InfModelEvidence.ReadDeclaredIds(largeInf),
            item => item.HardwareId == GpuId);

        folder.Add("nvidia/display.inf", largeInf);
        folder.Add("nvidia/verified.cat", "NOT A SIGNED CAT");
        folder.Add("killer/network.inf", Inf(KillerId));
        folder.Add("killer/verified.cat", "NOT A SIGNED CAT");
        folder.Add("audio/sound.inf", Inf(AudioId));
        folder.Add("audio/verified.cat", "NOT A SIGNED CAT");

        var plan = Plan(folder, Device(GpuId), Device(KillerId), Device(AudioId));
        Assert.Equal(3, plan.InfExamined);
        Assert.Equal(3, plan.MatchedInf);
        Assert.Equal(3, plan.Checks.Count);
        Assert.Equal(0, plan.UnreadableInf);
        Assert.Equal(0, plan.MissingCatalog);
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath) == "display.inf" &&
            Path.GetFileName(check.CatalogPath) == "verified.cat");
        Assert.True(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);
        Assert.DoesNotContain(folder.Root, string.Join(" ",
            plan.Checks.Select(check => Path.GetFileName(check.InfPath))));
    }

    [Fact]
    public void ExceedingTargetedMatchBudgetRejectsThatInfWithoutPartialSignatureChecks()
    {
        using var folder = new TemporaryFolder();
        // Simulate an untrusted INF with more matches than the separate
        // 4096 cap, after which no partial evidence may reach SignTool.
        var identifiers = Enumerable.Range(0,
                InfModelEvidence.MaximumTargetMatches + 1)
            .Select(i => $"PCI\\VEN_10DE&DEV_{i:X4}")
            .ToArray();
        var rows = string.Join("\n", identifiers.Select((id, i) =>
            $"Device{i}=Install,{id}"));
        var oversized = Inf(GpuId).Replace(
            $"Device=Install,{GpuId}",
            rows);
        folder.Add("nvidia/too-many.inf", oversized);
        folder.Add("nvidia/verified.cat", "NOT SIGNED");
        folder.Add("killer/good.inf", Inf(KillerId));
        folder.Add("killer/verified.cat", "NOT SIGNED");

        var broadDevice = Device(GpuId) with
        {
            HardwareIds = identifiers
        };
        var plan = Plan(folder, broadDevice, Device(KillerId));

        Assert.Equal(2, plan.InfExamined);
        Assert.Equal(1, plan.UnreadableInf);
        Assert.Equal(1, plan.MatchedInf);
        Assert.Single(plan.Checks);
        Assert.Equal("good.inf", Path.GetFileName(plan.Checks[0].InfPath));
        Assert.False(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);
    }

    [Fact]
    public void MalformedModelsDoesNotAbortOtherValidInfOrTreatPartialIdsAsVerified()
    {
        using var folder = new TemporaryFolder();
        folder.Add("nvidia/driver.inf", Inf(GpuId));
        folder.Add("nvidia/verified.cat", "NOT SIGNED");
        // The parser is allowed 64 physical-line continuations, and MUST
        // discard ALL evidence from this INF after a truncated/invalid tail.
        var corrupt = Inf(KillerId) + "\n[Malformed]\n" +
            "Repeated=Test,\\" + string.Concat(
                Enumerable.Repeat("\ntext,\\", 65)) + "\ntext\n";
        folder.Add("killer/broken.inf", corrupt);
        folder.Add("killer/verified.cat", "NOT SIGNED");

        var plan = Plan(folder, Device(GpuId), Device(KillerId));
        Assert.Equal(2, plan.InfExamined);
        Assert.Equal(1, plan.MatchedInf);
        Assert.Equal(1, plan.UnreadableInf);
        Assert.Equal(0, plan.MissingCatalog);
        Assert.Single(plan.Checks);
        Assert.Equal("driver.inf",
            Path.GetFileName(plan.Checks[0].InfPath));
        Assert.False(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);
    }

    [Fact]
    public void CatNotInSameDirectoryIsNotAValidPair()
    {
        using var folder = new TemporaryFolder();
        folder.Add("nvidia/driver.inf", Inf(GpuId));
        folder.Add("verified.cat", "CAT IN PARENT ONLY");

        var plan = Plan(folder, Device(GpuId));
        Assert.Equal(1, plan.MatchedInf);
        Assert.Equal(1, plan.MissingCatalog);
        Assert.Empty(plan.Checks);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void InfWithoutCatalogDirectiveIsNotTreatableAsSigned()
    {
        using var folder = new TemporaryFolder();
        folder.Add("gpu/driver.inf", Inf(GpuId, withCat: false));
        folder.Add("gpu/verified.cat", "EXISTS, BUT NOT REFERENCED");
        var plan = Plan(folder, Device(GpuId));

        Assert.Equal(1, plan.InfExamined);
        Assert.Equal(1, plan.MissingCatalog);
        Assert.Empty(plan.Checks);
    }

    [Fact]
    public void MalformedInfDoesNotEraseOtherValidEvidence()
    {
        using var folder = new TemporaryFolder();
        folder.Add("nvidia/driver.inf", Inf(GpuId));
        folder.Add("nvidia/verified.cat", "FAKE CAT");
        File.WriteAllBytes(folder.Add("nvidia/corrupt.inf", ""),
            [0xFF, 0xFE, 0x00, 0xD8]);

        var plan = Plan(folder, Device(GpuId));
        Assert.Equal(2, plan.InfExamined);
        Assert.Equal(1, plan.MatchedInf);
        Assert.Equal(1, plan.UnreadableInf);
        Assert.Single(plan.Checks);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void ExtraInfWithoutHardwareMatchCannotClaimVerification()
    {
        using var folder = new TemporaryFolder();
        folder.Add("nvidia/driver.inf", Inf(GpuId));
        folder.Add("nvidia/verified.cat", "FAKE CAT");
        var plan = Plan(folder, Device(KillerId));
        Assert.Equal(1, plan.InfExamined);
        Assert.Equal(0, plan.MatchedInf);
        Assert.Empty(plan.Checks);
    }

    [Fact]
    public void TooManyMatchingInfAreExplicitlyDeferred()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i < OemFolderCatalogAuditPlan.MaxChecks + 2; i++)
            folder.Add($"nvidia/driver{i:D2}.inf", Inf(GpuId));
        folder.Add("nvidia/verified.cat", "FAKE CAT");
        var plan = Plan(folder, Device(GpuId));

        Assert.Equal(14, plan.MatchedInf);
        Assert.Equal(OemFolderCatalogAuditPlan.MaxChecks, plan.Checks.Count);
        Assert.Equal(2, plan.DeferredInf);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void AuditPlansLateNvidiaKillerAndRealtekAfterNinetySixInf()
    {
        using var folder = new TemporaryFolder();
        // The old 96-INF cap failed before reaching these important
        // hardware-specific drivers, despite a successful folder comparison.
        for (var i = 0; i < 112; i++)
            folder.Add($"intel/irrelevant{i:D3}.inf",
                Inf($@"PCI\VEN_9999&DEV_{i:X4}"));

        const string realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        folder.Add("nvidia/gpu.inf", Inf(GpuId));
        folder.Add("nvidia/verified.cat", "SYNTHETIC CAT NOT SIGNED");
        folder.Add("killer/network.inf", Inf(KillerId));
        folder.Add("killer/verified.cat", "SYNTHETIC CAT NOT SIGNED");
        folder.Add("realtek/sound.inf", Inf(realtek));
        folder.Add("realtek/verified.cat", "SYNTHETIC CAT NOT SIGNED");

        var plan = Plan(folder, Device(GpuId), Device(KillerId), Device(realtek));

        Assert.Equal(115, plan.InfExamined);
        Assert.Equal(3, plan.MatchedInf);
        Assert.Equal(3, plan.Checks.Count);
        Assert.Equal(0, plan.DeferredInf);
        Assert.Equal(0, plan.UnreadableInf);
        Assert.Equal(0, plan.MissingCatalog);
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath) == "gpu.inf");
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath) == "network.inf");
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath) == "sound.inf");
        Assert.True(plan.IsComplete);
        Assert.False(plan.IsFullPackageVerified);
    }

    [Fact]
    public void TwelveSignatureSlotsFavorDifferentDevicesOverOneHundredRepeatInf()
    {
        using var folder = new TemporaryFolder();
        // Many NVIDIA drivers can use the same GPU ID and CAT.
        // A naive first-12 traversal would use every slot on that GPU.
        for (var i = 0; i < 110; i++)
            folder.Add($"nvidia/repeat{i:D3}.inf", Inf(GpuId));
        folder.Add("nvidia/verified.cat", "SYNTHETIC CAT");
        folder.Add("killer/driver.inf", Inf(KillerId));
        folder.Add("killer/verified.cat", "SYNTHETIC CAT");

        const string realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";
        folder.Add("realtek/codec.inf", Inf(realtek));
        folder.Add("realtek/verified.cat", "SYNTHETIC CAT");

        var plan = Plan(folder,
            Device(GpuId), Device(KillerId), Device(realtek));

        Assert.Equal(112, plan.InfExamined);
        Assert.Equal(112, plan.MatchedInf);
        Assert.Equal(OemFolderCatalogAuditPlan.MaxChecks, plan.Checks.Count);
        Assert.Equal(100, plan.DeferredInf);
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath) == "driver.inf");
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath) == "codec.inf");
        Assert.Contains(plan.Checks, check =>
            Path.GetFileName(check.InfPath).StartsWith("repeat",
                StringComparison.OrdinalIgnoreCase));
        Assert.False(plan.IsComplete);

        var notPerformed = plan.Checks.Select(_ =>
            new OemFolderCatalogAuditSummary.Row(
                CatalogTrustReport.Status.NotVerified,
                CatalogMemberVerification.Verdict.NotPerformed));
        var summary = OemFolderCatalogAuditSummary.Create(plan, notPerformed);
        Assert.Equal(0, summary.VerifiedInfMembers);
        Assert.Equal(100, summary.DeferredInf);
        Assert.Equal(12, summary.NotVerified);
        Assert.DoesNotContain(folder.Root, summary.Text);
        Assert.DoesNotContain("PRIVATE_INSTANCE", summary.Text);
    }

    [Fact]
    public void FourHundredMinusSixteenInfRemainWithinTheFolderAuditBudget()
    {
        using var folder = new TemporaryFolder();
        // The audit covers all 384 INF independently of the 12 signature
        // slots, but does not claim irrelevant files are verified.
        for (var i = 0; i < LocalOemFolderComparison.MaxFolderInfFiles - 1; i++)
            folder.Add($"irrelevant/driver{i:D3}.inf",
                Inf($@"PCI\VEN_9999&DEV_{i:X4}"));
        folder.Add("killer/target.inf", Inf(KillerId));
        folder.Add("killer/verified.cat", "SYNTHETIC CAT");

        var plan = Plan(folder, Device(KillerId));
        Assert.Equal(LocalOemFolderComparison.MaxFolderInfFiles, plan.InfExamined);
        Assert.Single(plan.Checks);
        Assert.Equal(1, plan.MatchedInf);
        Assert.Equal(0, plan.DeferredInf);
    }

    [Fact]
    public void MoreThan384InfsFailClosed()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i <= LocalOemFolderComparison.MaxFolderInfFiles; i++)
            folder.Add($"driver{i:D3}.inf", Inf(GpuId));
        Assert.Throws<InvalidDataException>(() =>
            Plan(folder, Device(GpuId)));
    }

    [Fact]
    public void JunctionOrSymbolicDirectoryFailsClosedIfSupported()
    {
        using var folder = new TemporaryFolder();
        folder.Add("one/driver.inf", Inf(GpuId));
        var link = Path.Combine(folder.Root, "symlink");
        try
        {
            Directory.CreateSymbolicLink(link,
                Path.Combine(folder.Root, "one"));
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<InvalidDataException>(() =>
            Plan(folder, Device(GpuId)));
    }

    [Fact]
    public void PreCancelledAuditCannotEnumerateFiles()
    {
        using var folder = new TemporaryFolder();
        folder.Add("one/driver.inf", Inf(GpuId));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            OemFolderCatalogAuditPlan.Create(folder.Root,
                [Device(GpuId)], "AMD64", cancelled.Token));
    }

    [Fact]
    public void FakeCatalogAndUnavailableSdkMustNeverBecomeVerified()
    {
        var plan = new OemFolderCatalogAuditPlan.Plan(
            2, 2, 0, 0, 0,
            [
                new OemFolderCatalogAuditPlan.Check("private.inf", "fake.cat"),
                new OemFolderCatalogAuditPlan.Check("private2.inf", "other.cat")
            ]);

        var result = OemFolderCatalogAuditSummary.Create(plan,
        [
            new(CatalogTrustReport.Status.InvalidSignature,
                CatalogMemberVerification.Verdict.NotPerformed),
            new(CatalogTrustReport.Status.ValidCatalogSignature,
                CatalogMemberVerification.Verdict.SdkToolUnavailable)
        ]);

        Assert.Equal(1, result.SignedCatalogs);
        Assert.Equal(0, result.VerifiedInfMembers);
        Assert.Equal(1, result.Rejected);
        Assert.Equal(1, result.NotVerified);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsFullPackageVerified);
        Assert.DoesNotContain("private", result.Text);
        Assert.Contains("NON attesta", result.Text);
    }

    [Fact]
    public void ValidCatWithoutInfMembershipDoesNotCountAsVerifiedInf()
    {
        var plan = new OemFolderCatalogAuditPlan.Plan(
            1, 1, 0, 0, 0,
            [new OemFolderCatalogAuditPlan.Check("secret.inf", "signed.cat")]);
        var onlyCat = OemFolderCatalogAuditSummary.Create(plan,
        [
            new(CatalogTrustReport.Status.ValidCatalogSignature,
                CatalogMemberVerification.Verdict.NotPerformed)
        ]);
        Assert.Equal(1, onlyCat.SignedCatalogs);
        Assert.Equal(0, onlyCat.VerifiedInfMembers);
        Assert.Equal(1, onlyCat.NotVerified);
    }

    [Fact]
    public void ValidSignatureAndMembershipStillDoNotVerifyEntirePackage()
    {
        var plan = new OemFolderCatalogAuditPlan.Plan(
            1, 1, 0, 0, 0,
            [new OemFolderCatalogAuditPlan.Check("secret.inf", "cat.cat")]);
        var report = OemFolderCatalogAuditSummary.Create(plan,
        [
            new(CatalogTrustReport.Status.ValidCatalogSignature,
                CatalogMemberVerification.Verdict.VerifiedInfMember)
        ]);
        Assert.Equal(1, report.VerifiedInfMembers);
        Assert.Equal(0, report.Rejected);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsFullPackageVerified);
        Assert.Contains("SYS/DLL", report.Text);
    }

    [Fact]
    public void NumberOfAuditedRowsMustMatchPlan()
    {
        var plan = new OemFolderCatalogAuditPlan.Plan(
            1, 1, 0, 0, 0,
            [new OemFolderCatalogAuditPlan.Check("secret.inf", "cat.cat")]);
        Assert.Throws<InvalidDataException>(() =>
            OemFolderCatalogAuditSummary.Create(plan, []));
    }
}
