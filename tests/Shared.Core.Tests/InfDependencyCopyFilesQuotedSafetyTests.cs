using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #499: CopyFiles and file-list records must use the same safe, quoted
/// field parser as SourceDisksNames/Files and AddService. Never elevate an
/// untrusted split token into a CAT membership audit candidate.
/// </summary>
public sealed class InfDependencyCopyFilesQuotedSafetyTests
{
    private static string Inf(string directive, string fileList = "",
        string sourceFiles = "") => $"""
        [Version]
        Signature="$Windows NT$"
        CatalogFile=driver.cat
        [DefaultInstall]
        CopyFiles={directive}
        [DriverCopy]
        {fileList}
        [SourceDisksNames]
        1="OEM, source media",,,
        [SourceDisksFiles.amd64]
        {sourceFiles}
        """;

    [Fact]
    public void QuotedDirectFilenameAndRenamedSourceRecoverNvidiaAndRealtek()
    {
        var inf = Inf(
            "\"@nvlddmkm.sys\",\"DriverCopy\"",
            "\"dest.dll\",\"rtk-source.dll\",,0x00000000",
            "nvlddmkm.sys=1\nrtk-source.dll=1,libs");
        var preview = InfDependencyPreview.Analyze(inf,
            ["nvlddmkm.sys", "libs/rtk-source.dll"], "x64");

        Assert.Equal(2, preview.References.Count);
        Assert.Equal(2, preview.PresentCount);
        Assert.Equal(0, preview.MissingCount);
        Assert.Equal(0, preview.UnresolvedCount);
        Assert.Contains(preview.References, r =>
            r.SourceName == "nvlddmkm.sys" &&
            r.RelativePath == "nvlddmkm.sys" &&
            r.Status == InfDependencyPreview.Presence.Present);
        Assert.Contains(preview.References, r =>
            r.SourceName == "rtk-source.dll" &&
            r.RelativePath == "libs/rtk-source.dll" &&
            r.Status == InfDependencyPreview.Presence.Present);
        Assert.False(preview.IsCompletePackage);
    }

    [Fact]
    public void QuotedSourceNameMayUseSingleSafeStringsExpansion()
    {
        var inf = Inf("\"DriverCopy\"",
            "\"rendered.dll\",\"%DriverBinary%\"",
            "%DriverBinary%=1") + "\n" + """
            [Strings]
            DriverBinary="killer-driver.dll"
            """;
        var preview = InfDependencyPreview.Analyze(
            inf, ["killer-driver.dll"], "amd64");
        var source = Assert.Single(preview.References);
        Assert.Equal("killer-driver.dll", source.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Present, source.Status);
        Assert.Equal("killer-driver.dll", source.RelativePath);
        Assert.False(preview.IsCompletePackage);
    }

    [Fact]
    public void MalformedQuoteSuffixCannotBecomeASignedPayloadCandidate()
    {
        var inf = Inf(
            "\"@nvidia.sys\"junk,@killer.sys",
            "",
            "nvidia.sys=1\nkiller.sys=1");
        var preview = InfDependencyPreview.Analyze(
            inf, ["nvidia.sys", "killer.sys"], "x64");
        Assert.Empty(preview.References);
        Assert.Equal(0, preview.PresentCount);
        Assert.Contains(preview.Warnings,
            warning => warning.Contains("CopyFiles"));
    }

    [Fact]
    public void MalformedFileListDestinationCannotInventASource()
    {
        var inf = Inf("DriverCopy",
            "\"destination.dll\"junk,killer.dll",
            "killer.dll=1");
        var preview = InfDependencyPreview.Analyze(
            inf, ["killer.dll"], "x64");
        Assert.Empty(preview.References);
        Assert.Equal(0, preview.PresentCount);
        Assert.Contains(preview.Warnings,
            warning => warning.Contains("Voce elenco CopyFiles"));
    }

    [Fact]
    public void QuotedCommaOrDoubleQuoteDoesNotSplitIntoFalseNames()
    {
        var inf = Inf(
            "\"@nvidia.sys,@killer.sys\",DriverCopy",
            "\"destination\"\"private.dll\",realtek.dll",
            "nvidia.sys=1\nkiller.sys=1\nrealtek.dll=1");
        var preview = InfDependencyPreview.Analyze(inf,
            ["nvidia.sys", "killer.sys", "realtek.dll"], "x64");
        Assert.Empty(preview.References);
        Assert.Equal(0, preview.PresentCount);
        Assert.NotEmpty(preview.Warnings);
        Assert.False(preview.IsCompletePackage);
    }

    [Fact]
    public void ValidMaximumEightyCopyFilesIsNotLimitedToEightCsvFields()
    {
        var direct = string.Join(",",
            Enumerable.Range(0, 80).Select(i => "@driver" + i + ".sys"));
        var disk = string.Join("\n",
            Enumerable.Range(0, 80).Select(i => "driver" + i + ".sys=1"));
        var inf = Inf(direct, "", disk);
        var report = InfDependencyPreview.Analyze(inf, [], "x64");

        Assert.Equal(80, report.References.Count);
        Assert.Equal(80, report.MissingCount);
        Assert.Equal(0, report.PresentCount);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void EightyOneCopyFilesStillFailClosedInsteadOfReturningPartial()
    {
        var direct = string.Join(",",
            Enumerable.Range(0, 81).Select(i => "@driver" + i + ".sys"));
        Assert.Throws<InvalidDataException>(() =>
            InfDependencyPreview.Analyze(Inf(direct), [], "x64"));
    }

    [Fact]
    public void QuotedPayloadReferencesReachRealReadOnlyCatalogAuditPlan()
    {
        var root = Directory.CreateTempSubdirectory("Omega-CopyFiles-499-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "libs"));
            File.WriteAllText(Path.Combine(root, "driver.inf"),
                Inf("\"@nvidia.sys\", \"DriverCopy\"",
                    "\"destination.dll\",\"realtek.dll\"",
                    "nvidia.sys=1\nrealtek.dll=1,libs"),
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root, "driver.cat"),
                "Synthetic unsigned CAT - not trusted");
            File.WriteAllText(Path.Combine(root, "nvidia.sys"), "SYNTHETIC SYS");
            File.WriteAllText(Path.Combine(root, "libs", "realtek.dll"),
                "SYNTHETIC DLL");

            var plan = OemReferencedPayloadAudit.Prepare(
                Path.Combine(root, "driver.inf"),
                Path.Combine(root, "driver.cat"), "x64");
            Assert.Equal(2, plan.References);
            Assert.Equal(2, plan.Present);
            Assert.Equal(0, plan.Missing);
            Assert.Equal(0, plan.Unresolved);
            Assert.Equal(2, plan.CandidatePaths.Count);
            Assert.Contains(Path.Combine(root, "nvidia.sys"), plan.CandidatePaths);
            Assert.Contains(Path.Combine(root, "libs", "realtek.dll"),
                plan.CandidatePaths);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MalformedCopyListNeverSendsExistingKillerDllToCatalogAudit()
    {
        var root = Directory.CreateTempSubdirectory("Omega-CopyFiles-499-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "driver.inf"),
                Inf("DriverCopy",
                    "\"destination.dll\"junk,killer.dll",
                    "killer.dll=1"));
            File.WriteAllText(Path.Combine(root, "driver.cat"),
                "Synthetic unsigned CAT - not trusted");
            File.WriteAllText(Path.Combine(root, "killer.dll"), "SYNTHETIC DLL");

            var plan = OemReferencedPayloadAudit.Prepare(
                Path.Combine(root, "driver.inf"),
                Path.Combine(root, "driver.cat"), "x64");
            Assert.Equal(0, plan.References);
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
