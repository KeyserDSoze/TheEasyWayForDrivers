using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #62: read-only AddService/ServiceBinary destination provenance must
/// never be confused with SourceDisks or signed CAT source membership.
/// </summary>
public sealed class InfDependencyServiceBinaryEvidenceTests
{
    [Fact]
    public void ServiceBinaryDestinationIsNotSourceOrPossibleMissingFile()
    {
        var inf = """
            [DefaultInstall.Services]
            AddService=OEM_NVIDIA,0,Nvidia.ServiceInstall
            [Nvidia.ServiceInstall]
            ServiceType=1
            ServiceBinary=%12%\nvlddmkm.sys
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");

        Assert.Empty(report.References);
        Assert.Equal(0, report.MissingCount);
        Assert.Equal(0, report.PresentCount);
        var hint = Assert.Single(report.ServiceBinaries);
        Assert.Equal("DefaultInstall.Services", hint.AddServiceSection);
        Assert.Equal("Nvidia.ServiceInstall", hint.ServiceInstallSection);
        Assert.Equal("nvlddmkm.sys", hint.DestinationFileName);
        Assert.Contains(report.EvidenceLines(), x =>
            x.Contains("SERVICEBINARY: DESTINAZIONI, NON SORGENTI (1)"));
        Assert.Contains(report.EvidenceLines(), x =>
            x.Contains("file sorgente NON dedotto"));
        Assert.False(report.IsCompletePackage);
        Assert.True(report.RequiresManualReview);
    }

    [Fact]
    public void IndependentCopyFilesCanBePresentWithoutProvingServiceSource()
    {
        var inf = """
            [DefaultInstall]
            CopyFiles=@killer.sys
            [DefaultInstall.Services]
            AddService=Killer,0,Killer_Install
            [Killer_Install]
            ServiceBinary=%12%\killer.sys
            [SourceDisksNames]
            1=OEM,,,
            [SourceDisksFiles]
            killer.sys=1
            """;
        var report = InfDependencyPreview.Analyze(inf, ["killer.sys"], "x64");

        var file = Assert.Single(report.References);
        Assert.Equal(InfDependencyPreview.Presence.Present, file.Status);
        Assert.Equal("DefaultInstall / CopyFiles", file.Origin);
        var destination = Assert.Single(report.ServiceBinaries);
        Assert.Equal("killer.sys", destination.DestinationFileName);
        Assert.Equal("DefaultInstall.Services", destination.AddServiceSection);
        Assert.False(report.IsCompletePackage);
        Assert.Contains(report.Warnings, x => x.Contains("ServiceBinary"));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\private.sys")]
    [InlineData(@"\\server\share\private.sys")]
    [InlineData(@"%12%\..\private.sys")]
    [InlineData(@"%SystemRoot%\private.sys")]
    [InlineData(@"%12%\\private.sys")]
    [InlineData(@"%12%\private.exe:ADS")]
    public void UnsafeOrUnknownServiceTargetIsUnresolvedNotAFileReference(
        string target)
    {
        var inf = "[DefaultInstall.Services]\nAddService=Test,0,Test_Service\n" +
            "[Test_Service]\nServiceBinary=" + target;
        var report = InfDependencyPreview.Analyze(inf, ["private.sys"], "x64");

        Assert.Empty(report.References);
        Assert.Null(Assert.Single(report.ServiceBinaries).DestinationFileName);
        Assert.Contains(report.Warnings, x =>
            x.Contains("ServiceBinary", StringComparison.Ordinal));
        Assert.DoesNotContain("private", report.Summary);
        Assert.DoesNotContain("private.sys", string.Join("\n", report.EvidenceLines()));
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void TargetDirIdCanContainSafeRelativeSubfolderButItIsStillDestination()
    {
        var inf = """
            [DefaultInstall.NTamd64.Services]
            AddService="Killer",0,Killer_Service
            [Killer_Service]
            ServiceBinary=%12%\drivers\killer.sys
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");

        var hint = Assert.Single(report.ServiceBinaries);
        Assert.Equal("killer.sys", hint.DestinationFileName);
        Assert.Equal("DefaultInstall.NTamd64.Services", hint.AddServiceSection);
        Assert.Empty(report.References);
        Assert.Equal(0, report.MissingCount);
    }

    [Fact]
    public void DuplicateServiceBinaryDirectivesAreNotResolvedFromFirstValue()
    {
        var inf = """
            [DefaultInstall.Services]
            AddService=Example,0,Example_Service
            [Example_Service]
            ServiceBinary=%12%\example.sys
            ServiceBinary=%12%\another.sys
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");
        Assert.Null(Assert.Single(report.ServiceBinaries).DestinationFileName);
        Assert.Empty(report.References);
        Assert.Contains(report.Warnings, x => x.Contains("duplicata"));
    }

    [Fact]
    public void RepeatedAddServiceCannotReenableAnAmbiguousServiceTarget()
    {
        var inf = """
            [DefaultInstall.Services]
            AddService=OEM,0,My_Service
            AddService=OEM,0,My_Service
            AddService=OEM,0,My_Service
            [My_Service]
            ServiceBinary=%12%\example.sys
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");
        Assert.Null(Assert.Single(report.ServiceBinaries).DestinationFileName);
        Assert.Empty(report.References);
        Assert.Contains(report.Warnings, x => x.Contains("ripetuto"));
    }

    [Fact]
    public void UnrelatedServicesAndMissingSectionsCannotCreateEvidence()
    {
        var inf = """
            [DefaultInstall.Services]
            AddService=One,0,UnknownService
            [NotReferenced]
            ServiceBinary=%12%\unrelated.sys
            """;
        var report = InfDependencyPreview.Analyze(inf, [], "x64");
        Assert.Empty(report.ServiceBinaries);
        Assert.Empty(report.References);
        Assert.Contains(report.Warnings, x => x.Contains("sezione servizio non risolta"));
    }

    [Fact]
    public void TooManyServicesFailClosedBeforeRenderingOrCatalogAudit()
    {
        var rows = string.Join("\n", Enumerable.Range(0, 33).Select(i =>
            $"AddService=S{i},0,S{i}Install"));
        var serviceSections = string.Join("\n", Enumerable.Range(0, 33)
            .Select(i => $"[S{i}Install]\nServiceBinary=%12%\\s{i}.sys"));
        var inf = "[DefaultInstall.Services]\n" + rows + "\n" + serviceSections;
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(inf, [], "x64"));
    }

    [Fact]
    public void ServiceBinaryOnlyDoesNotCreateCatAuditCandidates()
    {
        var root = Directory.CreateTempSubdirectory("Omega-ServiceBinary-").FullName;
        try
        {
            var infPath = Path.Combine(root, "driver.inf");
            var catPath = Path.Combine(root, "package.cat");
            File.WriteAllText(infPath, """
                [DefaultInstall.Services]
                AddService=IntelKiller,0,Killer_Service
                [Killer_Service]
                ServiceBinary=%12%\killer.sys
                """);
            File.WriteAllText(Path.Combine(root, "killer.sys"), "synthetic driver");
            File.WriteAllText(catPath, "unsigned synthetic catalog");

            var audit = OemReferencedPayloadAudit.Prepare(infPath, catPath, "x64");
            Assert.Equal(0, audit.Present);
            Assert.Equal(0, audit.Missing);
            Assert.Equal(0, audit.Unresolved);
            Assert.Empty(audit.CandidatePaths);
            Assert.False(audit.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
