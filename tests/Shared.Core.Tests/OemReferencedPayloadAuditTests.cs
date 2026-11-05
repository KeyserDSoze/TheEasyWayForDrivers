using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemReferencedPayloadAuditTests
{
    private sealed class PackageFolder : IDisposable
    {
        public string Root { get; } =
            Directory.CreateTempSubdirectory("OmegaDrive-PayloadAuditTest-").FullName;
        public string Add(string file, string data)
        {
            var full = Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, data, new UTF8Encoding(false));
            return full;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private const string InfWithPayloads = """
        [Version]
        Signature="$Windows NT$"
        CatalogFile=package.cat
        DriverVer=10/04/2026,32.0.15.7000
        [Manufacturer]
        Vendor=Models,NTamd64
        [Models.NTamd64]
        Device=Install,PCI\VEN_10DE&DEV_28A0
        [Install.NTamd64]
        CopyFiles=@driver.sys,CopyLib
        [CopyLib]
        copied.dll,library.dll
        missing.sys
        [SourceDisksNames]
        1=Media,,,
        [SourceDisksFiles.amd64]
        driver.sys=1
        library.dll=1,libs
        missing.sys=1
        """;

    private static OemReferencedPayloadAudit.Plan Plan(
        PackageFolder package, string inf = InfWithPayloads) =>
        OemReferencedPayloadAudit.Prepare(
            package.Add("display.inf", inf),
            package.Add("package.cat", "Not signed"),
            "x64");

    [Fact]
    public void FindsExactReferencedBinarySourcesAndMissingDependencies()
    {
        using var package = new PackageFolder();
        var driver = package.Add("driver.sys", "SYS MOCK");
        var library = package.Add("libs/library.dll", "DLL MOCK");
        package.Add("setup.exe", "NEVER EXECUTED");
        var plan = Plan(package);

        Assert.Equal(3, plan.References);
        Assert.Equal(2, plan.Present);
        Assert.Equal(1, plan.Missing);
        Assert.Equal(0, plan.Unresolved);
        Assert.Equal(0, plan.Deferred);
        Assert.False(plan.InventoryUnscannable);
        Assert.False(plan.IsPackageComplete);
        Assert.Equal(2, plan.CandidatePaths.Count);
        Assert.Contains(driver, plan.CandidatePaths);
        Assert.Contains(library, plan.CandidatePaths);
        Assert.DoesNotContain(plan.CandidatePaths,
            path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnresolvedSourceDoesNotBecomeMissingOrTrusted()
    {
        using var package = new PackageFolder();
        var inf = """
            [DefaultInstall]
            CopyFiles=@unknown.sys
            [Version]
            Signature="$Windows NT$"
            """;
        var plan = Plan(package, inf);
        Assert.Equal(1, plan.References);
        Assert.Equal(1, plan.Unresolved);
        Assert.Equal(0, plan.Missing);
        Assert.Empty(plan.CandidatePaths);
    }

    [Fact]
    public void ExistingUnreferencedFileIsNeverCheckedAgainstCatalog()
    {
        using var package = new PackageFolder();
        package.Add("unrelated.sys", "NOT IN INF");
        package.Add("driver.sys", "REFERENCED");
        var plan = Plan(package);
        Assert.Single(plan.CandidatePaths);
        Assert.EndsWith("driver.sys", plan.CandidatePaths[0]);
        Assert.Equal(1, plan.Missing);
    }

    [Fact]
    public void MissingOrDisabledSourceNeverMakesPackageVerified()
    {
        using var package = new PackageFolder();
        package.Add("driver.sys", "driver");
        var plan = Plan(package);
        var summary = OemReferencedPayloadAudit.Summarize(
        [
            new OemReferencedPayloadAudit.Reviewed(
                plan, [DriverPayloadAuditPolicy.Outcome.VerifiedMember], 0)
        ]);
        Assert.Equal(1, summary.VerifiedFileMembers);
        Assert.Equal(1, summary.MissingFiles);
        Assert.False(summary.IsPackageComplete);
        Assert.False(summary.IsInstallable);
        Assert.Contains("ranking Windows", summary.Text);
        Assert.DoesNotContain(package.Root, summary.Text);
    }

    [Fact]
    public void AnUnverifiedAndRejectedFileCannotBeCalledTrusted()
    {
        var plan = new OemReferencedPayloadAudit.Plan(
            3, 3, 0, 0, 0, 0, false, ["secret.sys", "secret.dll", "secret.dat"]);
        var summary = OemReferencedPayloadAudit.Summarize(
        [
            new OemReferencedPayloadAudit.Reviewed(
                plan,
                [
                    DriverPayloadAuditPolicy.Outcome.VerifiedMember,
                    DriverPayloadAuditPolicy.Outcome.Rejected,
                    DriverPayloadAuditPolicy.Outcome.NotVerified
                ],
                0)
        ]);
        Assert.Equal(1, summary.VerifiedFileMembers);
        Assert.Equal(1, summary.RejectedFileMembers);
        Assert.Equal(1, summary.NotVerifiedFileMembers);
        Assert.False(summary.IsInstallable);
        Assert.DoesNotContain("secret.sys", summary.Text);
    }

    [Fact]
    public void ChecksBeyondGlobalBudgetAreExplicitlyDeferred()
    {
        var plan = new OemReferencedPayloadAudit.Plan(
            3, 3, 0, 0, 0, 0, false, ["file1.sys", "file2.sys", "file3.sys"]);
        var summary = OemReferencedPayloadAudit.Summarize(
        [
            new OemReferencedPayloadAudit.Reviewed(
                plan, [DriverPayloadAuditPolicy.Outcome.VerifiedMember], 2)
        ]);
        Assert.Equal(2, summary.DeferredFileChecks);
        Assert.Equal(1, summary.VerifiedFileMembers);
        Assert.False(summary.IsPackageComplete);
    }

    [Fact]
    public void MissingOrUnexpectedResultsAreRejectedAsInconsistent()
    {
        var plan = new OemReferencedPayloadAudit.Plan(
            1, 1, 0, 0, 0, 0, false, ["one.sys"]);
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize(
            [
                new OemReferencedPayloadAudit.Reviewed(plan, [], 0)
            ]));
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize(
            [
                new OemReferencedPayloadAudit.Reviewed(
                    plan, [DriverPayloadAuditPolicy.Outcome.NotVerified], 1)
            ]));
    }

    [Fact]
    public void ExcessiveDirectoryInventoryDoesNotClaimZeroDependencies()
    {
        using var package = new PackageFolder();
        for (var i = 0; i <= DriverPayloadAuditPolicy.MaxInventoryEntries; i++)
            package.Add($"many/item{i:D3}.txt", "DATA");
        var plan = Plan(package);
        Assert.True(plan.InventoryUnscannable);
        Assert.Empty(plan.CandidatePaths);
        Assert.False(plan.IsPackageComplete);

        var summary = OemReferencedPayloadAudit.Summarize(
        [
            new OemReferencedPayloadAudit.Reviewed(plan, [], 0)
        ]);
        Assert.Equal(1, summary.UnanalyzedInf);
        Assert.Contains("1 INF non analizzati", summary.Text);
    }

    [Fact]
    public void ReparseDirectoryIsRejectedRatherThanFollowed()
    {
        using var package = new PackageFolder();
        package.Add("data/driver.sys", "mock");
        var inf = package.Add("display.inf", InfWithPayloads);
        var cat = package.Add("package.cat", "fake");
        var link = Path.Combine(package.Root, "extra");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(package.Root, "data"));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            return;
        }
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Prepare(inf, cat, "x64"));
    }

    [Fact]
    public void InfAndCatFromDifferentRootsCannotBePaired()
    {
        using var left = new PackageFolder();
        using var right = new PackageFolder();
        var inf = left.Add("display.inf", InfWithPayloads);
        var cat = right.Add("package.cat", "FAKE");
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Prepare(inf, cat, "x64"));
    }

    [Fact]
    public void VeryLargeReferencedBinaryIsDeferredNotSilentlyAccepted()
    {
        using var package = new PackageFolder();
        package.Add("driver.sys", "CONTENTS");
        var plan = Plan(package);
        Assert.False(plan.IsPackageComplete);
        Assert.True(plan.Missing > 0);
        // No file signature is claimed from mere local file existence.
        Assert.All(plan.CandidatePaths, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void CancelledPreviewCannotInventoryPackage()
    {
        using var package = new PackageFolder();
        var inf = package.Add("display.inf", InfWithPayloads);
        var cat = package.Add("package.cat", "CAT");
        using var token = new CancellationTokenSource();
        token.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            OemReferencedPayloadAudit.Prepare(inf, cat, "AMD64", token.Token));
    }
}
