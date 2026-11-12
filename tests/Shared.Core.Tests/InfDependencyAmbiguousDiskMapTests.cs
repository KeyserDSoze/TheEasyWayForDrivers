using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #397: Conflicting or invalid SourceDisks overrides cannot create
/// positive OEM payload evidence. This is a read-only preview, not CAT
/// verification, SetupAPI ranking, or permission to install.
/// </summary>
public sealed class InfDependencyAmbiguousDiskMapTests
{
    private const string Install = """
        [DefaultInstall]
        CopyFiles=@nvidia.sys
        """;

    [Fact]
    public void InvalidDecoratedDiskDirectoryMustShadowPresentGenericMapping()
    {
        var inf = Install + "\n" + """
            [SourceDisksNames]
            1=Generic,,,drivers
            [SourceDisksNames.amd64]
            1=Specific,,,\..\PRIVATE
            [SourceDisksFiles]
            nvidia.sys=1
            """;

        var result = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys"], "x64");

        var source = Assert.Single(result.References);
        Assert.Equal(InfDependencyPreview.Presence.Unresolved, source.Status);
        Assert.Null(source.RelativePath);
        Assert.Equal(0, result.PresentCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Equal(1, result.UnresolvedCount);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("Directory SourceDisksNames"));
        Assert.DoesNotContain("PRIVATE", result.Summary);
        Assert.False(result.IsCompletePackage);

        // This override only applies to the targeted architecture.
        var x86 = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys"], "x86");
        Assert.Equal(InfDependencyPreview.Presence.Present,
            Assert.Single(x86.References).Status);
    }

    [Theory]
    [InlineData("1=Old,,,old\n1=New,,,drivers")]
    [InlineData("1=New,,,drivers\n1=Old,,,old")]
    [InlineData("1=New,,,drivers\n1=New,,,drivers")]
    public void RepeatedDiskIdNeverInheritsTheLastPresentMapping(
        string diskLines)
    {
        var inf = Install + "\n[SourceDisksNames]\n" + diskLines +
            "\n[SourceDisksFiles]\nnvidia.sys=1";
        var result = InfDependencyPreview.Analyze(inf,
            ["old/nvidia.sys", "drivers/nvidia.sys"], "x64");

        Assert.Equal(InfDependencyPreview.Presence.Unresolved,
            Assert.Single(result.References).Status);
        Assert.Equal(0, result.PresentCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("duplicato"));
        Assert.False(result.IsCompletePackage);
    }

    [Theory]
    [InlineData("nvidia.sys=1\nNVIDIA.SYS=2")]
    [InlineData("NVIDIA.SYS=2\nnvidia.sys=1")]
    [InlineData("nvidia.sys=1\nnvidia.sys=1")]
    public void DuplicateSourceFilenameIsAmbiguousEvenWithDifferentCase(
        string fileLines)
    {
        var inf = Install + "\n" + """
            [SourceDisksNames]
            1=Old,,,old
            2=New,,,drivers
            [SourceDisksFiles]
            """ + "\n" + fileLines;

        var result = InfDependencyPreview.Analyze(inf,
            ["old/nvidia.sys", "drivers/nvidia.sys"], "x64");

        var found = Assert.Single(result.References);
        Assert.Equal(InfDependencyPreview.Presence.Unresolved, found.Status);
        Assert.Null(found.RelativePath);
        Assert.Equal(0, result.PresentCount);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("SourceDisksFiles duplicato"));
        Assert.False(result.IsCompletePackage);
    }

    [Fact]
    public void ThirdDuplicateCannotReenablePreviouslyInvalidatedMapping()
    {
        var inf = Install + "\n" + """
            [SourceDisksNames]
            1=A,,,old
            1=B,,,other
            1=C,,,drivers
            [SourceDisksFiles]
            nvidia.sys=1
            """;
        var result = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys"], "x64");

        Assert.Equal(InfDependencyPreview.Presence.Unresolved,
            Assert.Single(result.References).Status);
        Assert.Equal(0, result.PresentCount);
    }

    [Fact]
    public void ValidDecoratedDiskAndFileMappingsOverrideGenericSafely()
    {
        var inf = Install + "\n" + """
            [SourceDisksNames]
            1=Generic,,,old
            2=Another,,,other
            [SourceDisksNames.amd64]
            1=Specific,,,drivers
            [SourceDisksFiles]
            nvidia.sys=2
            [SourceDisksFiles.amd64]
            nvidia.sys=1
            """;

        var result = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys"], "x64");

        var found = Assert.Single(result.References);
        Assert.Equal(InfDependencyPreview.Presence.Present, found.Status);
        Assert.Equal("drivers/nvidia.sys", found.RelativePath);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("duplicato"));
        Assert.False(result.IsCompletePackage);
        Assert.True(result.RequiresManualReview);
    }
}
