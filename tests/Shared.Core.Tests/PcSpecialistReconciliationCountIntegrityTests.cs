using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #523: an OEM ZIP/CAB/PE-ZIP package's HTML reconciliation must never
/// claim more unique local PnP associations than the scanner can enumerate.
/// Neither the HTML nor these review-only counts authorize installation.
/// </summary>
public sealed class PcSpecialistReconciliationCountIntegrityTests
{
    private const string Name = "W11 Nvidia VGA.zip";
    private static PcSpecialistPortalSnapshot.Package Package() =>
        new(Name, "NVIDIA", "Solo consultazione");

    private static PcSpecialistPackageReconciliation.LocalSource Local(
        int pnp, int review = 0, bool examined = true) =>
        new(Name, examined, pnp, review, false);

    private static PcSpecialistPackageReconciliation.Report Compare(
        params PcSpecialistPackageReconciliation.LocalSource[] sources) =>
        PcSpecialistPackageReconciliation.Compare([Package()], sources);

    private static LocalOemDriverComparison.Report Archive(
        int devices, IReadOnlyList<LocalOemDriverComparison.Finding>? findings = null) =>
        new(1, devices, findings ?? [], 0, []);

    private static LocalOemDriverComparison.Finding Finding(int strength) =>
        new("NVIDIA GeForce RTX", "nv.inf", "31.0.0.1", "32.0.0.1",
            strength, InfPlatformPreflight.Verdict.ManualReviewRequired,
            LocalOemDriverComparison.VersionRelation.Unavailable);

    [Theory]
    [InlineData(2049, 0)]
    [InlineData(0, 2049)]
    [InlineData(2048, 1)]
    [InlineData(1, 2048)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(1, int.MaxValue)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void ImpossibleOrOverflowingLocalCountsAreRejected(
        int pnp, int review)
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            Compare(Local(pnp, review)));
        Assert.DoesNotContain(Name, ex.Message);
        Assert.DoesNotContain("NVIDIA", ex.Message);
    }

    [Fact]
    public void UnexaminedSourceCannotDeclarePnPOrWeakMatches()
    {
        Assert.Throws<InvalidDataException>(() =>
            Compare(Local(1, examined: false)));
        Assert.Throws<InvalidDataException>(() =>
            Compare(Local(0, 1, examined: false)));
        var unsupported = PcSpecialistPackageReconciliation.LocalSource
            .Unsupported(Name);
        Assert.Equal(PcSpecialistPackageReconciliation.State.UnsupportedFormat,
            Assert.Single(Compare(unsupported).Rows).Status);
        Assert.False(Compare(unsupported).IsInstallable);
    }

    [Fact]
    public void ExactBoundaryAndUnrelatedValidPackagesStayReadOnly()
    {
        var atLimit = Compare(Local(2040, 8));
        var match = Assert.Single(atLimit.Rows);
        Assert.Equal(PcSpecialistPackageReconciliation.State.DeclaredPnpMatch,
            match.Status);
        Assert.Contains("2040 dispositivi", match.Detail);
        Assert.False(match.IsInstallable);
        Assert.False(match.IsOfficiallyVerified);
        Assert.False(match.IsWindowsApplicable);
        Assert.False(atLimit.IsInstallable);

        var reviewOnly = Compare(Local(0, 2048));
        Assert.Equal(PcSpecialistPackageReconciliation.State.ReviewOnlyInf,
            Assert.Single(reviewOnly.Rows).Status);
        Assert.False(reviewOnly.IsComplete);
        Assert.DoesNotContain(Name, reviewOnly.Summary);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2049)]
    [InlineData(int.MaxValue)]
    public void ArchiveInventoryCannotExceedRealScannerBudget(int devices)
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.LocalSource.FromArchive(
                Name, Archive(devices)));
        Assert.DoesNotContain(Name, ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void UnknownHardwareMatchStrengthCannotBecomeVerifiedPnP(int strength)
    {
        var bad = Archive(1, [Finding(strength)]);
        var ex = Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.LocalSource.FromArchive(
                Name, bad));
        Assert.DoesNotContain("NVIDIA", ex.Message);
    }

    [Fact]
    public void MissingHalfOfFullOrdinalListsIsNotCompleteEvidence()
    {
        var baseReport = Archive(2, [Finding(2)]);
        foreach (var invalid in new[]
        {
            baseReport with
            {
                MatchedDeviceOrdinals = [0],
                PnpMatchedDeviceOrdinals = null
            },
            baseReport with
            {
                MatchedDeviceOrdinals = null,
                PnpMatchedDeviceOrdinals = [0]
            }
        })
        {
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistPackageReconciliation.LocalSource.FromArchive(
                    Name, invalid));
        }
    }

    [Fact]
    public void MoreVisibleFindingsThanScannedDevicesCannotInflateCoverage()
    {
        var bad = Archive(1, [Finding(2), Finding(0)]);
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.LocalSource.FromArchive(
                Name, bad));
        var omitted = Archive(1, [Finding(2)]) with
        {
            MatchingDevicesOmitted = 1
        };
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.LocalSource.FromArchive(
                Name, omitted));
    }

    [Fact]
    public void FullOrdinalEvidencePreservesLateNvidiaAndKillerMatches()
    {
        var report = Archive(120, Enumerable.Range(0, 80)
            .Select(i => Finding(i % 2 == 0 ? 2 : 0)).ToArray()) with
        {
            MatchingDevicesOmitted = 40,
            MatchedDeviceOrdinals = Enumerable.Range(0, 120).ToArray(),
            PnpMatchedDeviceOrdinals = Enumerable.Range(0, 60)
                .Select(i => i * 2).ToArray()
        };
        var local = PcSpecialistPackageReconciliation.LocalSource
            .FromArchive(Name, report);
        Assert.Equal(60, local.PnpMatches);
        Assert.Equal(60, local.ReviewOnlyMatches);
        Assert.True(local.Partial);

        var combined = Compare(local);
        Assert.Equal(1, combined.WithPnPEvidence);
        Assert.Contains("60 dispositivi", Assert.Single(combined.Rows).Detail);
        Assert.Contains("Analisi parziale", Assert.Single(combined.Rows).Detail);
        Assert.False(combined.IsInstallable);
    }

    [Fact]
    public void MalformedNullSourceCannotBecomeAReconciliation()
    {
        // Passing null! directly to a params method is a null
        // SOURCE ARRAY (ArgumentNullException), not a one-item array
        // containing a malformed LocalSource. Test the latter.
        Assert.Throws<InvalidDataException>(() =>
            Compare(new PcSpecialistPackageReconciliation.LocalSource[]
            {
                null!
            }));
        Assert.Throws<InvalidDataException>(() =>
            Compare(new PcSpecialistPackageReconciliation.LocalSource(
                null!, true, 1, 0, false)));
    }
}
