using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #399: UNC/device paths in INF SourceDisks metadata must never become
/// seemingly local SYS/DLL evidence or CAT audit candidates.
/// </summary>
public sealed class InfDependencyUncSourceDisksTests
{
    private static string Inf(string diskFolder, string fileSubdirectory = "") =>
        "[DefaultInstall]\nCopyFiles=@nvidia.sys\n" +
        "[SourceDisksNames]\n1=\"NVIDIA, Killer\",,," + diskFolder + "\n" +
        "[SourceDisksFiles.amd64]\nnvidia.sys=1," + fileSubdirectory;

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData(@"\/server\share")]
    [InlineData(@"/\server\share")]
    [InlineData(@"\\?\C:\private")]
    [InlineData(@"\\")]
    [InlineData("//")]
    public void UncOrRepeatedLeadingSeparatorsInDiskFolderCannotProducePresent(
        string diskFolder)
    {
        var report = InfDependencyPreview.Analyze(
            Inf(diskFolder), ["server/share/nvidia.sys",
                "private/nvidia.sys", "nvidia.sys"], "x64");

        var reference = Assert.Single(report.References);
        Assert.Equal(InfDependencyPreview.Presence.Unresolved, reference.Status);
        Assert.Null(reference.RelativePath);
        Assert.Equal(0, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.Equal(1, report.UnresolvedCount);
        Assert.Contains(report.Warnings, w =>
            w.Contains("SourceDisksNames", StringComparison.Ordinal));
        Assert.DoesNotContain("server", report.Summary);
        Assert.DoesNotContain("private", report.Summary);
        Assert.False(report.IsCompletePackage);
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData(@"\/server\share")]
    [InlineData(@"/\server\share")]
    [InlineData(@"\\")]
    public void UncSourceDisksFilesSubdirectoryDoesNotBecomeLocalCandidate(
        string subdirectory)
    {
        var report = InfDependencyPreview.Analyze(
            Inf("drivers", subdirectory),
            ["drivers/server/share/nvidia.sys", "drivers/nvidia.sys"],
            "x64");

        var reference = Assert.Single(report.References);
        Assert.Equal(InfDependencyPreview.Presence.Unresolved, reference.Status);
        Assert.Equal(0, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.Null(reference.RelativePath);
        Assert.Contains(report.Warnings, w =>
            w.Contains("sottocartella", StringComparison.Ordinal));
        Assert.False(report.IsCompletePackage);
    }

    [Theory]
    [InlineData(@"\WinNT", @"\drivers", "WinNT/drivers/nvidia.sys")]
    [InlineData("WinNT", "drivers", "WinNT/drivers/nvidia.sys")]
    [InlineData("drivers", "", "drivers/nvidia.sys")]
    public void SingleLeadingSeparatorAndRelativeFolderRemainSupported(
        string diskFolder, string subdirectory, string relativePath)
    {
        var report = InfDependencyPreview.Analyze(
            Inf(diskFolder, subdirectory), [relativePath], "x64");

        var reference = Assert.Single(report.References);
        Assert.Equal(InfDependencyPreview.Presence.Present, reference.Status);
        Assert.Equal(relativePath, reference.RelativePath);
        Assert.Equal(1, report.PresentCount);
        Assert.Equal(0, report.UnresolvedCount);
        Assert.False(report.IsCompletePackage);
        Assert.True(report.RequiresManualReview);
    }

    [Fact]
    public void LocalCatAuditNeverSelectsNamesakeBinaryFromUncDiskReference()
    {
        var root = Directory.CreateTempSubdirectory("Omega-UNC-").FullName;
        try
        {
            var infPath = Path.Combine(root, "display.inf");
            var catPath = Path.Combine(root, "package.cat");
            var nested = Path.Combine(root, "server", "share");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "nvidia.sys"), "MOCK");
            File.WriteAllText(infPath, Inf(@"\\server\share"));
            File.WriteAllText(catPath, "UNSIGNED TEST FIXTURE");

            var plan = OemReferencedPayloadAudit.Prepare(
                infPath, catPath, "x64");
            Assert.False(plan.InventoryUnscannable);
            Assert.Equal(1, plan.References);
            Assert.Equal(0, plan.Present);
            Assert.Equal(0, plan.Missing);
            Assert.Equal(1, plan.Unresolved);
            Assert.True(plan.WarningCount > 0);
            Assert.Empty(plan.CandidatePaths);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
