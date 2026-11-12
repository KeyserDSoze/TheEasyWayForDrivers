using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Regression #396: SourceDisksNames/Files use comma-separated fields
/// outside quotes. Local file presence is not CAT trust or permission
/// to stage/install a driver.
/// </summary>
public sealed class InfDependencyQuotedFieldsTests
{
    [Fact]
    public void CommaInQuotedDiskDescriptionKeepsNvidiaAndKillerPaths()
    {
        const string inf = """
            [DefaultInstall]
            CopyFiles=@nvidia.sys,@killer.sys
            [SourceDisksNames]
            1="Notebook, NVIDIA / Killer",,,drivers
            [SourceDisksFiles.amd64]
            nvidia.sys=1
            killer.sys=1
            """;

        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys", "drivers/killer.sys"], "amd64");

        Assert.Equal(2, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.Equal(0, report.UnresolvedCount);
        Assert.Contains(report.References, x =>
            x.SourceName == "nvidia.sys" &&
            x.RelativePath == "drivers/nvidia.sys");
        Assert.Contains(report.References, x =>
            x.SourceName == "killer.sys" &&
            x.RelativePath == "drivers/killer.sys");
        Assert.False(report.IsCompletePackage);
        Assert.DoesNotContain("PRIVATE", report.Summary);
    }

    [Fact]
    public void QuotedCabinetTagRemainsUnresolvedEvenIfPayloadAppearsOnDisk()
    {
        const string inf = """
            [DefaultInstall]
            CopyFiles=@nvidia.sys,@killer.sys
            [SourceDisksNames]
            1="Notebook, driver media","driverpack.cab",,drivers
            [SourceDisksFiles]
            nvidia.sys=1
            killer.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys", "drivers/killer.sys"], "x64");

        Assert.Equal(2, report.UnresolvedCount);
        Assert.Equal(0, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.All(report.References, x =>
        {
            Assert.Equal(InfDependencyPreview.Presence.Unresolved, x.Status);
            Assert.Null(x.RelativePath);
        });
        Assert.Contains(report.Warnings, x => x.Contains("CAB"));
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void EscapedDescriptionQuotesAndQuotedSourceSubdirectoryAreSupported()
    {
        const string inf = """
            [DefaultInstall]
            CopyFiles=@gpu.sys
            [SourceDisksNames]
            1="Notebook ""GPU"", Wi-Fi media",,,"drivers"
            [SourceDisksFiles.amd64]
            gpu.sys=1,"NVIDIA"
            """;
        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/NVIDIA/gpu.sys"], "x64");

        var item = Assert.Single(report.References);
        Assert.Equal(InfDependencyPreview.Presence.Present, item.Status);
        Assert.Equal("drivers/NVIDIA/gpu.sys", item.RelativePath);
        Assert.Equal(0, report.UnresolvedCount);
        Assert.False(report.IsCompletePackage);
    }

    [Theory]
    [InlineData("1=Notebook\"spoof\",,,drivers")]
    [InlineData("1=\"Notebook\"junk,,,drivers")]
    [InlineData("1=\"Notebook, media\",,," + "\"drivers,private\"")]
    public void MalformedOrUnsafeDiskMetadataNeverCreatesPresentEvidence(
        string diskLine)
    {
        var inf = "[DefaultInstall]\nCopyFiles=@nvidia.sys\n" +
            "[SourceDisksNames]\n" + diskLine + "\n" +
            "[SourceDisksFiles]\nnvidia.sys=1";
        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys", "nvidia.sys"], "x64");

        var source = Assert.Single(report.References);
        Assert.Equal(InfDependencyPreview.Presence.Unresolved, source.Status);
        Assert.Equal(0, report.PresentCount);
        Assert.True(report.Warnings.Count > 0);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void MalformedSourceDisksFilesCannotOverrideGenericValidMapping()
    {
        const string inf = """
            [DefaultInstall]
            CopyFiles=@nvidia.sys
            [SourceDisksNames]
            1=Media,,,drivers
            [SourceDisksFiles]
            nvidia.sys=1
            [SourceDisksFiles.amd64]
            nvidia.sys=1,"sub"junk
            """;
        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys"], "x64");

        Assert.Equal(InfDependencyPreview.Presence.Unresolved,
            Assert.Single(report.References).Status);
        Assert.Equal(0, report.PresentCount);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void PlainSourceDisksRegressionPreservesNormalPathsAndCabPolicy()
    {
        const string inf = """
            [DefaultInstall]
            CopyFiles=@gpu.sys,@audio.sys
            [SourceDisksNames]
            1=Media,,,drivers
            2=Other,packed.cab,,
            [SourceDisksFiles]
            gpu.sys=1
            audio.sys=2
            """;
        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/gpu.sys", "audio.sys"], "x64");

        Assert.Equal(1, report.PresentCount);
        Assert.Equal(1, report.UnresolvedCount);
        Assert.Equal(0, report.MissingCount);
        Assert.False(report.IsCompletePackage);
    }
}
