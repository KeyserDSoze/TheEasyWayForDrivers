using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #398: repeated INF [Strings] keys cannot bind a SYS/DLL source
/// to whichever declaration happened to appear first. Read-only evidence,
/// not an OEM signature, SetupAPI ranking, or installation decision.
/// </summary>
public sealed class InfDependencyDuplicateStringsTests
{
    private static string Inf(params string[] definitions) =>
        "[DefaultInstall]\nCopyFiles=@%Binary%\n" +
        "[SourceDisksNames]\n1=Media,,,\n" +
        "[SourceDisksFiles]\n%Binary%=1\n" +
        "[Strings]\n" + string.Join("\n", definitions);

    [Theory]
    [InlineData("Binary=\"nvidia.sys\"", "binary=\"killer.sys\"")]
    [InlineData("Binary=\"killer.sys\"", "binary=\"nvidia.sys\"")]
    [InlineData("Binary=\"nvidia.sys\"", "Binary=\"nvidia.sys\"")]
    public void ConflictingOrIdenticalCaseInsensitiveDuplicatesCannotProvePresence(
        string first, string second)
    {
        var result = InfDependencyPreview.Analyze(
            Inf(first, second), ["nvidia.sys", "killer.sys"], "x64");

        Assert.Empty(result.References);
        Assert.Equal(0, result.PresentCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Contains(result.Warnings, x => x.Contains(
            "[Strings]", StringComparison.Ordinal));
        Assert.False(result.IsCompletePackage);
        Assert.True(result.RequiresManualReview);
        Assert.DoesNotContain("nvidia.sys", result.Summary);
        Assert.DoesNotContain("killer.sys", result.Summary);
    }

    [Fact]
    public void ThirdDuplicateCannotResurrectAmbiguousToken()
    {
        var result = InfDependencyPreview.Analyze(Inf(
            "Binary=\"nvidia.sys\"",
            "binary=\"killer.sys\"",
            "BINARY=\"nvidia.sys\""), ["nvidia.sys", "killer.sys"], "x64");

        Assert.Empty(result.References);
        Assert.Equal(0, result.PresentCount);
        Assert.Contains(result.Warnings, x => x.Contains("[Strings]"));
        Assert.False(result.IsCompletePackage);
    }

    [Fact]
    public void InvalidFirstDefinitionFollowedByValidOneIsStillAmbiguous()
    {
        var result = InfDependencyPreview.Analyze(Inf(
            "Binary=\"%PRIVATE%\"",
            "Binary=\"nvidia.sys\""), ["nvidia.sys"], "x64");

        Assert.Empty(result.References);
        Assert.Equal(0, result.PresentCount);
        Assert.Contains(result.Warnings, x => x.Contains("[Strings]"));
        Assert.DoesNotContain("PRIVATE", result.Summary);
    }

    [Fact]
    public void UniqueLiteralStringStillResolvesOrdinarySourceOnly()
    {
        var result = InfDependencyPreview.Analyze(
            Inf("Binary=\"killer.sys\""), ["killer.sys"], "x64");

        var reference = Assert.Single(result.References);
        Assert.Equal("killer.sys", reference.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Present, reference.Status);
        Assert.Equal(0, result.MissingCount);
        Assert.DoesNotContain(result.Warnings, x => x.Contains("[Strings]"));
        Assert.False(result.IsCompletePackage);
    }

    [Fact]
    public void DuplicateStringsNeverSelectAnyCatMembershipCandidate()
    {
        var root = Directory.CreateTempSubdirectory("Omega-Strings-").FullName;
        try
        {
            var infPath = Path.Combine(root, "display.inf");
            var catPath = Path.Combine(root, "package.cat");
            File.WriteAllText(infPath, Inf(
                "Binary=\"nvidia.sys\"", "binary=\"killer.sys\""));
            File.WriteAllText(catPath, "NOT A SIGNED CATALOG");
            File.WriteAllText(Path.Combine(root, "nvidia.sys"), "SYNTHETIC");
            File.WriteAllText(Path.Combine(root, "killer.sys"), "SYNTHETIC");

            var plan = OemReferencedPayloadAudit.Prepare(
                infPath, catPath, "x64");

            Assert.False(plan.InventoryUnscannable);
            Assert.Equal(0, plan.Present);
            Assert.Empty(plan.CandidatePaths);
            Assert.True(plan.WarningCount > 0);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
