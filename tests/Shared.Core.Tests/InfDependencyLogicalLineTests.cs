using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Regression #395: dependency preview must follow the same conservative
/// physical-to-logical INF parsing as PnP evidence, without claiming
/// installability, CAT trust, or complete SetupAPI dependency resolution.
/// </summary>
public sealed class InfDependencyLogicalLineTests
{
    [Fact]
    public void QuotedSemicolonAndContinuedCopyFilesKeepNvidiaAndKillerSourcesPresent()
    {
        // Synthetic INF with two current-architecture PnP models. All
        // sources must be derived from INF directives, not device names.
        const string inf = """
            [Manufacturer]
            Vendor=Models,NTamd64
            [Models.NTamd64]
            GPU=GpuInstall,PCI\VEN_10DE&DEV_28A0
            WiFi=WifiInstall,PCI\VEN_8086&DEV_51F1
            [GpuInstall.NTamd64]
            CopyFiles=@nvidia.sys,\
                @nvaudio.sys ; comment on the continued line
            [WifiInstall.NTamd64]
            CopyFiles=@killer.sys
            [SourceDisksNames]
            1="OEM; local package",,,drivers ; comment outside quotes
            [SourceDisksFiles.amd64]
            nvidia.sys=1
            nvaudio.sys=1
            killer.sys=1
            """;

        var report = InfDependencyPreview.Analyze(inf,
            ["drivers/nvidia.sys", "drivers/nvaudio.sys", "drivers/killer.sys"],
            "AMD64");

        Assert.Equal(3, report.References.Count);
        Assert.Equal(3, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.Equal(0, report.UnresolvedCount);
        Assert.Contains(report.References, item =>
            item.SourceName == "nvidia.sys" &&
            item.RelativePath == "drivers/nvidia.sys" &&
            item.Origin.Contains("GpuInstall", StringComparison.Ordinal));
        Assert.Contains(report.References, item =>
            item.SourceName == "nvaudio.sys" &&
            item.RelativePath == "drivers/nvaudio.sys");
        Assert.Contains(report.References, item =>
            item.SourceName == "killer.sys" &&
            item.RelativePath == "drivers/killer.sys" &&
            item.Origin.Contains("WifiInstall", StringComparison.Ordinal));
        Assert.False(report.IsCompletePackage);
        Assert.True(report.RequiresManualReview);
        Assert.DoesNotContain("PRIVATE", report.Summary);
    }

    [Fact]
    public void ContinuedNeedsPreservesAllLocalFileListsWithoutExternalInclude()
    {
        const string inf = """
            [DefaultInstall]
            Needs=GpuCopy,\
                KillerCopy
            [GpuCopy]
            CopyFiles=@nvidia.sys
            [KillerCopy]
            CopyFiles=@killer.sys
            [SourceDisksNames]
            1="Local; OEM",,,
            [SourceDisksFiles]
            nvidia.sys=1
            killer.sys=1
            """;

        var report = InfDependencyPreview.Analyze(inf,
            ["nvidia.sys", "killer.sys"], "x64");

        Assert.Equal(2, report.References.Count);
        Assert.Equal(2, report.PresentCount);
        Assert.Equal(0, report.MissingCount);
        Assert.Contains(report.References, x => x.SourceName == "nvidia.sys");
        Assert.Contains(report.References, x => x.SourceName == "killer.sys");
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void SemicolonOutsideQuotesRemainsACommentNotAPayloadReference()
    {
        const string inf = """
            [DefaultInstall]
            CopyFiles=@real.sys ; CopyFiles=@fake.sys
            [SourceDisksNames]
            1="OEM; media",,,
            [SourceDisksFiles]
            real.sys=1 ; fake.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf, ["real.sys"], "x64");

        var reference = Assert.Single(report.References);
        Assert.Equal("real.sys", reference.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Present, reference.Status);
        Assert.DoesNotContain(report.References, x => x.SourceName == "fake.sys");
        Assert.False(report.IsCompletePackage);
    }

    [Theory]
    [InlineData("[DefaultInstall]\nCopyFiles=@nvidia.sys,\\")]
    [InlineData("[SourceDisksNames]\n1=\"OEM; unbalanced,,,drivers\n[SourceDisksFiles]\na.sys=1")]
    [InlineData("[DefaultInstall]\nCopyFiles=@a.sys,\"unbalanced")]
    public void TruncatedContinuationOrUnclosedQuoteFailsClosed(string inf)
    {
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(inf, ["nvidia.sys"], "x64"));
    }

    [Fact]
    public void ExistingPhysicalLineAndLogicalLineLimitsRemainEnforced()
    {
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(new string('\n', 12000), [], "x64"));

        var longDirective = "[DefaultInstall]\nCopyFiles=@" +
            new string('a', 4096) + ".sys";
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(longDirective, [], "x64"));

        var tooManyContinuations = "[DefaultInstall]\nCopyFiles=@" +
            string.Concat(Enumerable.Repeat("a\\\n", 65)) + "driver.sys";
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(tooManyContinuations, [], "x64"));

        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(
                "[DefaultInstall]\nCopyFiles=@a.sys\n" +
                "[DefaultInstall]\nCopyFiles=@b.sys", [], "x64"));
    }
}
