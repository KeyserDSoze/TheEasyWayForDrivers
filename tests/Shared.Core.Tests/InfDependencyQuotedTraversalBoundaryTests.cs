using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #503: Manufacturer, Models and Needs must validate entire quoted CSV-INF
/// directives before accepting local CopyFiles dependencies. This does NOT
/// infer which Windows install section SetupAPI would actually select.
/// </summary>
public sealed class InfDependencyQuotedTraversalBoundaryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static string Inf(
        string manufacturer = "Vendor=Models,NTamd64",
        string model = "Device=Install,PCI\\VEN_10DE&DEV_28A0",
        string install = "CopyFiles=@nvidia.sys",
        string additionalSections = "") => $"""
        [Version]
        Signature="$Windows NT$"
        [Manufacturer]
        {manufacturer}
        [Models.NTamd64]
        {model}
        [Install.NTamd64]
        {install}
        [SourceDisksNames]
        1=OEM,,,
        [SourceDisksFiles]
        nvidia.sys=1
        killer.sys=1
        realtek.dll=1
        {additionalSections}
        """;

    private static InfDependencyPreview.Report Inspect(string inf) =>
        InfDependencyPreview.Analyze(inf,
            ["nvidia.sys", "killer.sys", "realtek.dll"], "x64");

    [Theory]
    [InlineData("Vendor=Models,NTamd64,\"NTarm64\"junk")]
    [InlineData("Vendor=\"Models\"suffix,NTamd64")]
    [InlineData("Vendor=Models\"suffix\",NTamd64")]
    [InlineData("Vendor=Models,NTamd64,\"NTbad,extra\"")]
    public void MalformedManufacturerCannotEnableAnInstallSection(
        string manufacturer)
    {
        var preview = Inspect(Inf(manufacturer: manufacturer));
        Assert.Empty(preview.References);
        Assert.Equal(0, preview.PresentCount);
        Assert.Contains(preview.Warnings, text =>
            text.Contains("Manufacturer", StringComparison.Ordinal));
        Assert.False(preview.IsCompletePackage);
        Assert.DoesNotContain("PRIVATE_", preview.Summary);
    }

    [Theory]
    [InlineData("Device=Install,PCI\\VEN_10DE&DEV_28A0,\"PCI\\VEN_8086&DEV_51F1\"junk")]
    [InlineData("Device=Install,PCI\\VEN_10DE&DEV_28A0,PCI\\VEN_8086&DEV_51F1\"\"")]
    [InlineData("Device=\"Install\"suffix,PCI\\VEN_10DE&DEV_28A0")]
    [InlineData("Device=Install,PCI\\VEN_10DE&DEV_28A0,bad\"alias\"")]
    public void MalformedModelsAliasCannotGenerateCopyFilesEvidence(
        string model)
    {
        var preview = Inspect(Inf(model: model));
        Assert.Empty(preview.References);
        Assert.Contains(preview.Warnings, text =>
            text.Contains("Models", StringComparison.Ordinal));
        Assert.False(preview.IsCompletePackage);
    }

    [Theory]
    [InlineData("Needs=Inner,\"Bad\"junk")]
    [InlineData("Needs=Inner,Extra\"\"")]
    [InlineData("Needs=\"Inner,Hidden\",Inner")]
    [InlineData("Needs=\"Inner\"\"Suffix\",Inner")]
    public void InvalidNeedsListCannotFollowEarlierValidTarget(string needs)
    {
        var inf = Inf(install: needs, additionalSections: """
            [Inner]
            CopyFiles=@killer.sys
            """);
        var report = Inspect(inf);
        Assert.Empty(report.References);
        Assert.Equal(0, report.PresentCount);
        Assert.Contains(report.Warnings, s => s.Contains("Needs"));
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void QuotedValidNeedsAndContinuedLinesCanFollowTwoLocalSections()
    {
        var inf = Inf(
            manufacturer: "Vendor=\"Models\",\"NTamd64\"",
            model: $"Device=\"Install\",\"{Nvidia}\",\"{Realtek}\"",
            install: "Needs=\"Inner\",\\\n\"Second\"",
            additionalSections: """
                [Inner]
                CopyFiles="@killer.sys"
                [Second]
                CopyFiles="@realtek.dll"
                """);
        var report = Inspect(inf);
        Assert.Equal(2, report.References.Count);
        Assert.Equal(2, report.PresentCount);
        Assert.Contains(report.References, row =>
            row.SourceName == "killer.sys" &&
            row.Status == InfDependencyPreview.Presence.Present);
        Assert.Contains(report.References, row =>
            row.SourceName == "realtek.dll" &&
            row.Status == InfDependencyPreview.Presence.Present);
        Assert.False(report.IsCompletePackage);
        Assert.DoesNotContain("PRIVATE_", report.Summary);
    }

    [Fact]
    public void ExternalIncludeKeepsNeedsInManualReviewNotLocalResolution()
    {
        var inf = Inf(install: """
            Include=external.inf
            Needs="Inner"
            """, additionalSections: """
            [Inner]
            CopyFiles=@killer.sys
            """);
        var preview = Inspect(inf);
        Assert.Empty(preview.References);
        Assert.Contains(preview.Warnings, s =>
            s.Contains("Include rilevato"));
        Assert.False(preview.IsCompletePackage);
    }

    [Fact]
    public void ValidDirectCopyFilesRemainsIndependentOfNeeds()
    {
        var inf = Inf(install: """
            Needs="Inner","Bad"junk
            CopyFiles="@nvidia.sys"
            """, additionalSections: """
            [Inner]
            CopyFiles=@killer.sys
            """);
        var preview = Inspect(inf);
        var only = Assert.Single(preview.References);
        Assert.Equal("nvidia.sys", only.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Present, only.Status);
        Assert.Contains(preview.Warnings, s => s.Contains("Needs"));
        Assert.False(preview.IsCompletePackage);
    }

    [Fact]
    public void MalformedNeedsCannotReachSignedPayloadAuditCandidatePaths()
    {
        var root = Directory.CreateTempSubdirectory("Omega-NeedsQuote-503-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "driver.inf"),
                Inf(install: "Needs=Inner,\"Bad\"junk",
                    additionalSections: """
                        [Inner]
                        CopyFiles=@killer.sys
                        """), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "driver.cat"),
                "unsigned synthetic CAT");
            File.WriteAllText(Path.Combine(root, "killer.sys"),
                "synthetic driver payload");

            var plan = OemReferencedPayloadAudit.Prepare(
                Path.Combine(root, "driver.inf"),
                Path.Combine(root, "driver.cat"), "x64");
            Assert.Equal(0, plan.Present);
            Assert.Equal(0, plan.References);
            Assert.Empty(plan.CandidatePaths);
            Assert.True(plan.WarningCount > 0);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InvalidManufacturerCannotPassQuotedNeedsThroughFilesystem()
    {
        var root = Directory.CreateTempSubdirectory("Omega-ManQuote-503-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "driver.inf"),
                Inf(manufacturer: "Vendor=Models,NTamd64,\"Bad\"junk",
                    install: "Needs=Inner",
                    additionalSections: """
                        [Inner]
                        CopyFiles=@killer.sys
                        """));
            File.WriteAllText(Path.Combine(root, "driver.cat"),
                "unsigned synthetic CAT");
            File.WriteAllText(Path.Combine(root, "killer.sys"), "synthetic");

            var plan = OemReferencedPayloadAudit.Prepare(
                Path.Combine(root, "driver.inf"),
                Path.Combine(root, "driver.cat"), "x64");
            Assert.Empty(plan.CandidatePaths);
            Assert.Equal(0, plan.References);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
