using System.Text;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #506: [Strings] quoted scalars must never be trimmed into a different
/// SYS/DLL basename before CopyFiles and local CAT membership planning.
/// </summary>
public sealed class InfDependencyStringsQuotedScalarTests
{
    private static string Inf(string binary, string additionalStrings = "",
        string copyFiles = "@%Binary%") =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[DefaultInstall]\nCopyFiles=" + copyFiles + "\n" +
        "[SourceDisksNames]\n1=OEM,,,\n" +
        "[SourceDisksFiles.amd64]\n%Binary%=1\n" +
        (additionalStrings.Length > 0 ? "%Other%=1\n" : "") +
        "[Strings]\nBinary=" + binary + "\n" + additionalStrings;

    [Theory]
    [InlineData("\"\"\"nvidia.sys\"\"\"")] // escaped literal quotes around filename
    [InlineData("\"\"nvidia.sys\"\"")]     // balanced but malformed field
    [InlineData("\"nvidia.sys\"junk\"\"")] // suffix after closed quote
    [InlineData("nvidia.sys\"suffix\"")]  // embedded illegal quotes
    [InlineData("\"nvidia.sys,other.sys\"")] // one quoted but unsafe filename
    public void QuotedOrMalformedStringsCannotCreatePresentPayload(
        string binary)
    {
        var report = InfDependencyPreview.Analyze(
            Inf(binary), ["nvidia.sys", "other.sys"], "x64");

        Assert.Empty(report.References);
        Assert.Equal(0, report.PresentCount);
        Assert.Contains(report.Warnings, warning =>
            warning.Contains("Strings", StringComparison.Ordinal) ||
            warning.Contains("sorgente", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("CopyFiles", StringComparison.Ordinal));
        Assert.False(report.IsCompletePackage);
        Assert.DoesNotContain("PRIVATE_SYNTHETIC", report.Summary);
    }

    [Theory]
    [InlineData("\"nvidia.sys\"")]
    [InlineData("nvidia.sys")]
    [InlineData("  \"nvidia.sys\"  ")]
    public void NormalQuotedAndPlainTokensPreserveSourceEvidence(string binary)
    {
        var report = InfDependencyPreview.Analyze(
            Inf(binary), ["nvidia.sys"], "amd64");

        var reference = Assert.Single(report.References);
        Assert.Equal("nvidia.sys", reference.SourceName);
        Assert.Equal("nvidia.sys", reference.RelativePath);
        Assert.Equal(InfDependencyPreview.Presence.Present, reference.Status);
        Assert.Equal(1, report.PresentCount);
        Assert.DoesNotContain(report.Warnings, warning =>
            warning.Contains("[Strings]", StringComparison.Ordinal));
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void EscapedQuoteTokenCannotHideIndependentValidRealtekSource()
    {
        var report = InfDependencyPreview.Analyze(Inf(
            "\"\"\"nvidia.sys\"\"\"", additionalStrings: "Other=\"realtek.dll\"",
            copyFiles: "@%Binary%,@%Other%"),
            ["nvidia.sys", "realtek.dll"], "x64");

        var valid = Assert.Single(report.References);
        Assert.Equal("realtek.dll", valid.SourceName);
        Assert.Equal(InfDependencyPreview.Presence.Present, valid.Status);
        Assert.Equal(1, report.PresentCount);
        Assert.Contains(report.Warnings, text => text.Contains("[Strings]"));
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void LiteralNestedTokenIsNotRecursivelyExpanded()
    {
        var report = InfDependencyPreview.Analyze(Inf(
            "\"%Other%\"", additionalStrings: "Other=\"nvidia.sys\""),
            ["nvidia.sys"], "x64");

        Assert.Empty(report.References);
        Assert.Equal(0, report.PresentCount);
        Assert.False(report.IsCompletePackage);
    }

    [Fact]
    public void EscapedQuoteCannotEnterPhysicalReadOnlyCatCandidateList()
    {
        var folder = Directory.CreateTempSubdirectory(
            "Omega-StringsQuotes-506-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "driver.inf"),
                Inf("\"\"\"nvidia.sys\"\"\""), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "driver.cat"),
                "SYNTHETIC UNSIGNED CATALOG");
            File.WriteAllText(Path.Combine(folder, "nvidia.sys"),
                "SYNTHETIC PAYLOAD");

            var plan = OemReferencedPayloadAudit.Prepare(
                Path.Combine(folder, "driver.inf"),
                Path.Combine(folder, "driver.cat"), "x64");

            Assert.Equal(0, plan.Present);
            Assert.Equal(0, plan.References);
            Assert.Empty(plan.CandidatePaths);
            Assert.True(plan.WarningCount > 0);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OrdinaryValidTokenStillYieldsReadOnlyCatCandidate()
    {
        var folder = Directory.CreateTempSubdirectory(
            "Omega-StringsNormal-506-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "driver.inf"),
                Inf("\"nvidia.sys\""), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "driver.cat"),
                "SYNTHETIC UNSIGNED CATALOG");
            File.WriteAllText(Path.Combine(folder, "nvidia.sys"),
                "SYNTHETIC PAYLOAD");

            var plan = OemReferencedPayloadAudit.Prepare(
                Path.Combine(folder, "driver.inf"),
                Path.Combine(folder, "driver.cat"), "x64");

            Assert.Equal(1, plan.Present);
            Assert.Equal(1, plan.References);
            Assert.Single(plan.CandidatePaths);
            Assert.False(plan.IsPackageComplete);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
