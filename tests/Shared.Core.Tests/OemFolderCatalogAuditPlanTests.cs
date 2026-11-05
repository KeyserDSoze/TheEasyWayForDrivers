using System.Text;
using TheEasyWayForDrivers.Core.Models;
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
    public void TooManyInfsFailClosed()
    {
        using var folder = new TemporaryFolder();
        for (var i = 0; i <= OemZipInfEvidence.MaxInfFiles; i++)
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
