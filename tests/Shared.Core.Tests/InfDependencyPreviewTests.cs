using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class InfDependencyPreviewTests
{
    private const string NormalInf = """
        [Version]
        Signature="$Windows NT$"
        [Manufacturer]
        %Vendor%=Models,NTamd64
        [Models.NTamd64]
        %Device%=MyInstall, PCI\VEN_8086&DEV_1234
        [MyInstall.NTamd64]
        CopyFiles=@base.sys, DriverCopy
        [DriverCopy]
        renamed.dll,original.dll
        extra.sys
        [SourceDisksNames]
        1 = %Media%,,,
        [SourceDisksFiles.amd64]
        base.sys=1
        original.dll=1,drivers
        extra.sys=1
        """;

    [Fact]
    public void DetectsMissingSourceNotDestinationForRenamedCopyFile()
    {
        var report = InfDependencyPreview.Analyze(NormalInf,
            ["base.sys", "drivers/original.dll"], "x64");
        Assert.Equal(3, report.References.Count);
        Assert.Equal(1, report.MissingCount);
        Assert.Equal("extra.sys", Assert.Single(report.References
            .Where(x => x.Status == InfDependencyPreview.Presence.Missing)).SourceName);
        var renamed = Assert.Single(report.References.Where(x => x.SourceName == "original.dll"));
        Assert.Equal("drivers/original.dll", renamed.RelativePath);
        Assert.Equal(InfDependencyPreview.Presence.Present, renamed.Status);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void AbsentSourceDisksMappingIsUnresolvedNotReportedMissing()
    {
        var inf = """
            [Manufacturer]
            Vendor=Models,NTamd64
            [Models.NTamd64]
            A=Install,PCI\VEN_8086&DEV_1111
            [Install.NTamd64]
            CopyFiles=@unknown.sys
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");
        Assert.Equal(0, report.MissingCount);
        Assert.Equal(1, report.UnresolvedCount);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void OnlyCurrentArchitectureSourceAndInstallSectionsAreChecked()
    {
        var inf = """
            [Manufacturer]
            Vendor=Models,NTamd64,NTarm64
            [Models.NTamd64]
            A=InstallA,PCI\VEN_8086&DEV_1000
            [Models.NTarm64]
            B=InstallB,PCI\VEN_8086&DEV_1000
            [InstallA.NTamd64]
            CopyFiles=@intel.sys
            [InstallB.NTarm64]
            CopyFiles=@arm.sys
            [SourceDisksNames]
            1=Media,,,
            [SourceDisksFiles.amd64]
            intel.sys=1
            [SourceDisksFiles.arm64]
            arm.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");
        var result = Assert.Single(report.References);
        Assert.Equal("intel.sys", result.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Missing, result.Status);
    }

    [Fact]
    public void IncludeNeedsAndCopyInfAreExplicitlyUnresolved()
    {
        var inf = """
            [DefaultInstall]
            Include=machine.inf
            Needs=OtherInstall
            CopyINF=other.inf
            CopyFiles=@driver.sys
            [SourceDisksNames]
            1=Media,,,
            [SourceDisksFiles]
            driver.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf, ["driver.sys"], "x64");
        Assert.Contains(report.Warnings, x => x.Contains("Include/Needs"));
        Assert.Contains(report.Warnings, x => x.Contains("CopyINF"));
        Assert.Equal(0, report.MissingCount);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void CabSourceIsUnresolvedEvenWhenNamedFileAppearsOnDisk()
    {
        var inf = """
            [DefaultInstall]
            CopyFiles=@driver.sys
            [SourceDisksNames]
            1=Media,packed.cab,,
            [SourceDisksFiles]
            driver.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf, ["driver.sys"], "x64");
        Assert.Equal(InfDependencyPreview.Presence.Unresolved,
            Assert.Single(report.References).Status);
    }

    [Fact]
    public void SourceDisksNamesAndFilesSubdirectoriesResolveRelativeToPackageRoot()
    {
        var inf = """
            [DefaultInstall]
            CopyFiles=@engine.sys
            [SourceDisksNames]
            1=Media,,,\WinNT
            [SourceDisksFiles.amd64]
            engine.sys=1,\drivers
            """;
        var report = InfDependencyPreview.Analyze(inf,
            [@"WinNT\drivers\engine.sys"], "AMD64");
        var entry = Assert.Single(report.References);
        Assert.Equal("WinNT/drivers/engine.sys", entry.RelativePath);
        Assert.Equal(InfDependencyPreview.Presence.Present, entry.Status);
    }

    [Fact]
    public void UnrelatedCopyFilesSectionDoesNotCreateFalseMissingDependency()
    {
        var inf = """
            [Manufacturer]
            Vendor=Models,NTamd64
            [Models.NTamd64]
            A=Correct,PCI\VEN_8086&DEV_1000
            [Correct.NTamd64]
            CopyFiles=@present.sys
            [Unrelated.NTamd64]
            CopyFiles=@fake_missing.sys
            [SourceDisksNames]
            1=Media,,,
            [SourceDisksFiles]
            present.sys=1
            fake_missing.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf, ["present.sys"], "x64");
        Assert.Single(report.References);
        Assert.Equal("present.sys", report.References[0].SourceName);
    }

    [Theory]
    [InlineData("..\\evil.sys")]
    [InlineData(@"sub\..\payload.sys")]
    [InlineData(@"C:\temp\evil.sys")]
    [InlineData(@"\\server\evil.sys")]
    [InlineData("ab%token%.sys")]
    public void MalformedCopyFilenameNeverBecomesAResolvablePath(string unsafeName)
    {
        var inf = "[DefaultInstall]\nCopyFiles=@" + unsafeName +
                  "\n[SourceDisksNames]\n1=Media,,,\n[SourceDisksFiles]\n" +
                  unsafeName + "=1";
        var report = InfDependencyPreview.Analyze(inf, [], "x64");
        Assert.Empty(report.References);
        Assert.NotEmpty(report.Warnings);
    }

    [Fact]
    public void RejectsUnsafeInventoryPath()
    {
        Assert.Throws<InvalidDataException>(() => InfDependencyPreview.Analyze(
            NormalInf, ["sub/../foo.sys"], "x64"));
    }

    [Fact]
    public void RepeatedSectionsAreFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => InfDependencyPreview.Analyze(
            "[DefaultInstall]\nCopyFiles=@x.sys\n[DefaultInstall]\nCopyFiles=@y.sys", [], "x64"));
    }

    [Fact]
    public void TooManyDependenciesFailClosed()
    {
        var files = string.Join(",", Enumerable.Range(0, 81).Select(i => "@file" + i + ".sys"));
        Assert.Throws<InvalidDataException>(() => InfDependencyPreview.Analyze(
            "[DefaultInstall]\nCopyFiles=" + files, [], "x64"));
    }

    [Fact]
    public void UnsupportedInfWithNoCopyFilesDoesNotClaimComplete()
    {
        var report = InfDependencyPreview.Analyze(
            "[DefaultInstall]\nInclude=system.inf\nNeeds=Base", [], "x64");
        Assert.Empty(report.References);
        Assert.NotEmpty(report.Warnings);
        Assert.False(report.IsCompletePackage);
    }
}
