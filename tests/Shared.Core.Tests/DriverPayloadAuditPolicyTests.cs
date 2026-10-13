using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverPayloadAuditPolicyTests
{
    private static DriverPayloadAuditPolicy.LocalEntry Entry(
        string name, long size = 42, bool reparse = false) =>
        new(name, size, reparse);

    [Fact]
    public void CollectsRelevantExtensionsInNestedFoldersButNeverClaimsCompleteness()
    {
        var plan = DriverPayloadAuditPolicy.CreatePlan([
            Entry("driver.inf"), Entry("sub/audio.sys"), Entry("sub/codec.dll"),
            Entry("install.exe"), Entry("firmware.bin"),
            Entry("readme.txt"), Entry("source/notes.xml"), Entry("driver.cat")]);
        Assert.Equal(5, plan.Candidates.Count);
        Assert.Equal(3, plan.OtherFiles);
        Assert.False(plan.IsFullPackageVerified);
    }

    [Theory]
    [InlineData("../bad.sys")]
    [InlineData("sub/../bad.sys")]
    [InlineData("/absolute.sys")]
    [InlineData(@"C:\driver.sys")]
    [InlineData(@"\\server\driver.sys")]
    [InlineData("sub//file.sys")]
    [InlineData("sub/./file.sys")]
    [InlineData("data\0file.sys")]
    public void RejectsTraversalAndUnsafePaths(string name) =>
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan([Entry(name)]));

    [Fact]
    public void RejectsReparsePointsEvenWhenFileTypeIsExcluded() =>
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan([Entry("manual.txt", reparse: true)]));

    [Fact]
    public void RejectsCaseInsensitiveDuplicates() =>
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan([Entry("Audio.sys"), Entry("audio.sys")]));

    [Fact]
    public void RejectsTooManyRelevantFiles() =>
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan(Enumerable.Range(0, 25)
                .Select(n => Entry($"file{n}.sys"))));

    [Fact]
    public void RejectsOversizedOrNegativeFiles()
    {
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan([
                Entry("huge.sys", DriverPayloadAuditPolicy.MaxIndividualBytes + 1)]));
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan([Entry("negative.inf", -1)]));
    }

    [Fact]
    public void RejectsOversizedInventoryEvenWhenNotAllFilesAreRelevant() =>
        Assert.Throws<InvalidDataException>(() =>
            DriverPayloadAuditPolicy.CreatePlan(Enumerable.Range(0, 257)
                .Select(n => Entry($"file{n}.txt"))));

    [Fact]
    public void AggregationIsAlwaysPartialEvenWhenAllVisibleFilesVerify()
    {
        var findings = new[] {
            new DriverPayloadAuditPolicy.Finding("driver.inf", "AAA",
                DriverPayloadAuditPolicy.Outcome.VerifiedMember),
            new DriverPayloadAuditPolicy.Finding("driver.sys", "BBB",
                DriverPayloadAuditPolicy.Outcome.VerifiedMember)
        };
        var summary = DriverPayloadAuditPolicy.Summarize(findings, 0);
        Assert.Equal(2, summary.Verified);
        Assert.False(summary.IsPackageInstallable);
        Assert.Contains("NON valida", summary.Explanation);
    }

    [Fact]
    public void RejectionAndUnavailableToolNeverBecomeVerified()
    {
        var summary = DriverPayloadAuditPolicy.Summarize([
            new("good.inf", "", DriverPayloadAuditPolicy.Outcome.VerifiedMember),
            new("bad.sys", "", DriverPayloadAuditPolicy.Outcome.Rejected),
            new("unknown.dll", "", DriverPayloadAuditPolicy.Outcome.NotVerified)], 1);
        Assert.Equal(1, summary.Verified);
        Assert.Equal(1, summary.Rejected);
        Assert.Equal(1, summary.NotVerified);
        Assert.Equal(1, summary.OtherFiles);
        Assert.False(summary.IsPackageInstallable);
    }
}
