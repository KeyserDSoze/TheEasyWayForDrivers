using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #518: CAT/SYS/DLL membership summaries must not create more evidence
/// than the bounded Windows signature reader was permitted to check.
/// These are read-only aggregate claims, never install authorization.
/// </summary>
public sealed class OemPayloadMembershipSummaryIntegrityTests
{
    private static OemReferencedPayloadAudit.Reviewed Review(
        int count, DriverPayloadAuditPolicy.Outcome outcome =
            DriverPayloadAuditPolicy.Outcome.NotVerified)
    {
        var paths = Enumerable.Range(0, count)
            .Select(i => $"PRIVATE_Payload_{i:D2}.sys").ToArray();
        return new(
            new OemReferencedPayloadAudit.Plan(
                count, count, 0, 0, 0, 0, false, paths),
            Enumerable.Repeat(outcome, count).ToArray(), 0);
    }

    [Fact]
    public void EightVerifiedAcrossTwelveInfPlusUntrustedNotVerifiedAreAllowed()
    {
        // Untrusted INF/CAT pairs can legitimately have eight non-checked
        // NotVerified candidate rows each. Their presence must not be
        // mistaken for performing 96 individual signature checks.
        var reviews = new[]
        {
            Review(8, DriverPayloadAuditPolicy.Outcome.VerifiedMember)
        }.Concat(Enumerable.Range(0, 11).Select(_ =>
            Review(8))).ToArray();

        var result = OemReferencedPayloadAudit.Summarize(reviews);
        Assert.Equal(96, result.ReferencedFiles);
        Assert.Equal(96, result.PresentFiles);
        Assert.Equal(8, result.VerifiedFileMembers);
        Assert.Equal(88, result.NotVerifiedFileMembers);
        Assert.Equal(0, result.RejectedFileMembers);
        Assert.Equal(0, result.DeferredFileChecks);
        Assert.False(result.IsPackageComplete);
        Assert.False(result.IsInstallable);
        Assert.DoesNotContain("PRIVATE_", result.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NinthVerifiedOrRejectedMemberIsBeyondGlobalEightFileBudget(
        bool ninthRejected)
    {
        var ninth = ninthRejected
            ? DriverPayloadAuditPolicy.Outcome.Rejected
            : DriverPayloadAuditPolicy.Outcome.VerifiedMember;
        var data = new[]
        {
            Review(8, DriverPayloadAuditPolicy.Outcome.VerifiedMember),
            Review(1, ninth)
        };
        var exception = Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize(data));
        Assert.DoesNotContain("PRIVATE", exception.Message);
    }

    [Fact]
    public void MoreThanTwelveInfIsRefusedBeforeSummingAnyEvidence()
    {
        var rows = Enumerable.Range(0,
                OemFolderCatalogAuditPlan.MaxChecks + 1)
            .Select(_ => Review(0)).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize(rows));
    }

    [Fact]
    public void MoreThanEightCandidatesInOneInfIsRefused()
    {
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([
                Review(OemReferencedPayloadAudit.MaxFilesPerInf + 1)
            ]));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(81, 81, 0, 0, 0)]
    [InlineData(2, 1, 0, 0, 0)]
    [InlineData(1, 0, 1, 1, 0)]
    [InlineData(1, 1, 0, 0, -1)]
    public void NegativeOverBudgetOrContradictoryPlanCountersFailClosed(
        int referenced, int present, int missing,
        int unresolved, int warning)
    {
        var plan = new OemReferencedPayloadAudit.Plan(
            referenced, present, missing, unresolved,
            warning, 0, false, []);
        var reviewed = new OemReferencedPayloadAudit.Reviewed(
            plan, [], 0);
        var ex = Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([reviewed]));
        Assert.DoesNotContain("PRIVATE", ex.Message);
    }

    [Fact]
    public void UnscannableInventoryCannotReportPositiveMembership()
    {
        var malformed = Review(1,
            DriverPayloadAuditPolicy.Outcome.VerifiedMember) with
        {
            Plan = new OemReferencedPayloadAudit.Plan(
                1, 1, 0, 0, 0, 0, true,
                ["PRIVATE_UNSCANNABLE.sys"])
        };
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([malformed]));

        var valid = OemReferencedPayloadAudit.Summarize([
            new OemReferencedPayloadAudit.Reviewed(
                OemReferencedPayloadAudit.Plan.NotAnalyzed(), [], 0)
        ]);
        Assert.Equal(1, valid.UnanalyzedInf);
        Assert.Equal(0, valid.VerifiedFileMembers);
        Assert.False(valid.IsInstallable);
    }

    [Fact]
    public void UnknownOutcomeCannotDisappearFromVerificationTotals()
    {
        var invalid = Review(1) with
        {
            MembershipResults = [(DriverPayloadAuditPolicy.Outcome)999]
        };
        var error = Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([invalid]));
        Assert.DoesNotContain("PRIVATE_", error.Message);
    }

    [Fact]
    public void DuplicatePhysicalMemberPathsAreNotIndependentChecks()
    {
        var plan = new OemReferencedPayloadAudit.Plan(
            2, 2, 0, 0, 0, 0, false,
            ["PRIVATE_Driver.sys", "private_driver.SYS"]);
        var row = new OemReferencedPayloadAudit.Reviewed(
            plan,
            [DriverPayloadAuditPolicy.Outcome.VerifiedMember,
             DriverPayloadAuditPolicy.Outcome.VerifiedMember],
            0);
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([row]));
    }

    [Fact]
    public void DeferredAndMembershipCardinalityMustMatchPerInfBudget()
    {
        var plan = new OemReferencedPayloadAudit.Plan(
            3, 3, 0, 0, 0, 0, false,
            ["PRIVATE_1.sys", "PRIVATE_2.sys", "PRIVATE_3.sys"]);
        var singleResult = new[]
        {
            DriverPayloadAuditPolicy.Outcome.VerifiedMember
        };
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([
                new OemReferencedPayloadAudit.Reviewed(
                    plan, singleResult, 0)
            ]));
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([
                new OemReferencedPayloadAudit.Reviewed(
                    plan, singleResult, -1)
            ]));

        var valid = OemReferencedPayloadAudit.Summarize([
            new OemReferencedPayloadAudit.Reviewed(
                plan, singleResult, 2)
        ]);
        Assert.Equal(1, valid.VerifiedFileMembers);
        Assert.Equal(2, valid.DeferredFileChecks);
    }

    [Fact]
    public void NullCollectionsAndMissingPlanCannotYieldAnAudit()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OemReferencedPayloadAudit.Summarize(null!));

        var valid = Review(1);
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([
                valid with { Plan = null! }
            ]));
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([
                valid with { MembershipResults = null! }
            ]));
        Assert.Throws<InvalidDataException>(() =>
            OemReferencedPayloadAudit.Summarize([
                valid with {
                    Plan = valid.Plan with { CandidatePaths = null! }
                }
            ]));
    }

    [Fact]
    public void ZeroChecksAndZeroInfRemainNonInstallable()
    {
        var result = OemReferencedPayloadAudit.Summarize([]);
        Assert.Equal(0, result.VerifiedFileMembers);
        Assert.Equal(0, result.ReferencedFiles);
        Assert.False(result.IsPackageComplete);
        Assert.False(result.IsInstallable);
    }
}
