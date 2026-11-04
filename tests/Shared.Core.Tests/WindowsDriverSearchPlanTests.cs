using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsDriverSearchPlanTests
{
    [Fact]
    public void RecommendedModeDoesNotExpandUserSelectedSources()
    {
        var passes = WindowsDriverSearchPlan.Create(
            DriverSearchMode.Recommended, microsoftUpdateRegistered: true);

        var pass = Assert.Single(passes);
        Assert.Equal("Windows configurato", pass.Name);
        Assert.Null(pass.ServerSelection);
        Assert.Null(pass.ServiceId);
        Assert.False(pass.IsAdvancedCandidate);
        Assert.Equal("IsInstalled=0 and IsHidden=0 and Type='Driver'", pass.Criteria);
    }

    [Fact]
    public void ComprehensiveModeWithoutMuRetainsOriginalFourQueries()
    {
        var passes = WindowsDriverSearchPlan.Create(
            DriverSearchMode.Comprehensive, microsoftUpdateRegistered: false);

        Assert.Equal(4, passes.Count);
        Assert.Equal(new[]
        {
            "Windows configurato",
            "Windows Update online",
            "Windows configurato · avanzato",
            "Windows Update online · avanzato"
        }, passes.Select(pass => pass.Name));
        Assert.Equal(2, passes.Count(pass => pass.IsAdvancedCandidate));
        Assert.All(passes, pass => Assert.Null(pass.ServiceId));

        var notRegistered = WindowsDriverSearchPlan.NotRegisteredStatus();
        Assert.False(notRegistered.Succeeded);
        Assert.Equal("NotRegistered", notRegistered.Outcome);
        Assert.Contains("non interrogata", notRegistered.Detail);
        Assert.Equal(0, notRegistered.Candidates);
    }

    [Fact]
    public void ComprehensiveModeWithAlreadyRegisteredMuAddsTwoDistinctSources()
    {
        var passes = WindowsDriverSearchPlan.Create(
            DriverSearchMode.Comprehensive, microsoftUpdateRegistered: true);

        Assert.Equal(6, passes.Count);
        Assert.Equal(6, passes.Select(pass => pass.Name).Distinct().Count());
        var mu = passes.Where(pass =>
            pass.Name.StartsWith("Microsoft Update", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, mu.Length);
        Assert.Equal(3, mu[0].ServerSelection);
        Assert.Equal(3, mu[1].ServerSelection);
        Assert.Equal(WindowsDriverSearchPlan.MicrosoftUpdateServiceId, mu[0].ServiceId);
        Assert.Equal(WindowsDriverSearchPlan.MicrosoftUpdateServiceId, mu[1].ServiceId);
        Assert.Equal(WindowsDriverSearchPlan.RecommendedCriteria, mu[0].Criteria);
        Assert.False(mu[0].IncludePotentiallySuperseded);
        Assert.False(mu[0].IsAdvancedCandidate);
        Assert.Equal(WindowsDriverSearchPlan.BroadCriteria, mu[1].Criteria);
        Assert.True(mu[1].IncludePotentiallySuperseded);
        Assert.True(mu[1].IsAdvancedCandidate);

        Assert.All(passes, pass => Assert.DoesNotContain("Software", pass.Criteria));
    }

    [Theory]
    [InlineData("7971f918-a847-4430-9279-4a52d1efe18d", true)]
    [InlineData("{7971F918-A847-4430-9279-4A52D1EFE18D}", true)]
    [InlineData("9482f4b4-e343-43b6-b170-9a65bc822c77", false)]
    [InlineData("7971f918-a847-4430-9279-4a52d1efe18e", false)]
    [InlineData("7971f918-a847-4430-9279-4a52d1efe18d.evil", false)]
    [InlineData("", false)]
    public void OnlyExactGuidIdentifiesRegisteredMicrosoftUpdateService(
        string id, bool expected)
    {
        Assert.Equal(expected,
            WindowsDriverSearchPlan.IsMicrosoftUpdateRegistered([id]));
    }

    [Fact]
    public void EmptyServiceListAndRegistrationFailureAreNotZeroDriverSearches()
    {
        Assert.False(WindowsDriverSearchPlan.IsMicrosoftUpdateRegistered([]));
        var failure = WindowsDriverSearchPlan.RegistrationFailureStatus();
        Assert.Equal("Failed", failure.Outcome);
        Assert.False(failure.Succeeded);
        Assert.Contains("non verificata", failure.Detail);
    }

    [Fact]
    public void UnregisteredSourceDoesNotMarkCompletedWuaAsFailed()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows configurato", "Completed", 0, "No drivers"),
            new DriverSearchPassStatus("Windows Update online", "Completed", 0, "No drivers"),
            WindowsDriverSearchPlan.NotRegisteredStatus()
        ]);
        Assert.False(report.AllFailed);
        Assert.False(report.IsPartial);
    }

    [Fact]
    public void ServiceRegistrationErrorIsPartialAndNotZeroCandidateProof()
    {
        var report = new DriverSearchReport([],
        [
            new DriverSearchPassStatus("Windows configurato", "Completed", 1, "OK"),
            WindowsDriverSearchPlan.RegistrationFailureStatus()
        ]);
        Assert.True(report.IsPartial);
        Assert.False(report.AllFailed);
    }
}
