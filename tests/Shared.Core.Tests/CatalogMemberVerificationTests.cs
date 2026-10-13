using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class CatalogMemberVerificationTests
{
    [Fact]
    public void OnlyZeroExitCodeReportsSpecificInfMembershipVerified()
    {
        var result = CatalogMemberVerification.FromSignToolExitCode(0);
        Assert.True(result.IsInfMemberVerified);
        Assert.Contains("QUESTO INF", result.Explanation);
        Assert.Contains("NON sono stati verificati", result.Explanation);
        Assert.Contains("non autorizzato", result.Explanation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(2148204800)]
    public void AnyNonzeroExitCodeIsRejected(int code)
    {
        var result = CatalogMemberVerification.FromSignToolExitCode(code);
        Assert.Equal(CatalogMemberVerification.Verdict.Rejected, result.Result);
        Assert.False(result.IsInfMemberVerified);
    }

    [Fact]
    public void MissingSdkNeverReportsValidation()
    {
        var result = CatalogMemberVerification.MissingSdk();
        Assert.False(result.IsInfMemberVerified);
        Assert.Contains("NON verificata", result.Explanation);
    }

    [Fact]
    public void NotPerformedIsNotVerified() =>
        Assert.False(CatalogMemberVerification.NotChecked().IsInfMemberVerified);

    [Fact]
    public void WindowsSdkLocatorOnlyBuildsVersionedSdkPathsOrderedNewestFirst()
    {
        var candidate = WindowsSdkSignToolLocator.CandidatePaths(
            @"C:\Program Files (x86)", "x64",
            ["10.0.22000.0", "10.0.26100.0", "..\\evil", "non-sdk", "10.0.22621.0"]);
        Assert.Equal(3, candidate.Count);
        Assert.EndsWith(@"10.0.26100.0\x64\signtool.exe", candidate[0]);
        Assert.EndsWith(@"10.0.22621.0\x64\signtool.exe", candidate[1]);
        Assert.DoesNotContain(candidate, path => path.Contains("evil"));
    }

    [Theory]
    [InlineData("ARM64", @"arm64\signtool.exe")]
    [InlineData("x86", @"x86\signtool.exe")]
    public void LocatorHandlesSupportedArchitectures(string arch, string suffix)
    {
        var candidate = Assert.Single(WindowsSdkSignToolLocator.CandidatePaths(
            @"C:\Program Files (x86)", arch, ["10.0.26100.0"]));
        Assert.EndsWith(suffix, candidate);
    }

    [Fact]
    public void UnknownArchitectureCannotGuessExecutable() =>
        Assert.Empty(WindowsSdkSignToolLocator.CandidatePaths(
            @"C:\Program Files (x86)", "SomethingElse", ["10.0.26100.0"]));
}
