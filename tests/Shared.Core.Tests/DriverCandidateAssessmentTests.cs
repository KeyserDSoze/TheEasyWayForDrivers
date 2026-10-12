using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverCandidateAssessmentTests
{
    private static DriverInfo Device(string? provider = "Intel", string? version = "2.0.0.0") =>
        new(@"PCI\VEN_8086&DEV_1234", "Test", "Vendor", "System",
            provider, version, null, null, true, true, 0,
            [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
            [@"PCI\VEN_8086&DEV_1234"]);

    private static DriverUpdateInfo Update(
        string? provider = "Intel", string? version = "3.0.0.0",
        string? id = @"PCI\VEN_8086&DEV_1234&SUBSYS_00000001") =>
        new("guid", "Device Driver", null, null, provider, version,
            null, false, null, null, id, null);

    [Fact]
    public void ExactIdMatchingProviderAndHigherVersion_ReportsNewerButNotCertified()
    {
        var result = DriverCandidateAssessment.Evaluate(Device(), Update());
        Assert.Equal(2, result.HardwareEvidence);
        Assert.Equal(DriverCandidateAssessment.VersionRelation.Newer, result.Relation);
        Assert.Contains("non garantisce", result.Summary);
    }

    [Fact]
    public void OlderVersion_IndicatesPossibleDowngrade() =>
        Assert.Equal(DriverCandidateAssessment.VersionRelation.Older,
            DriverCandidateAssessment.Evaluate(Device(), Update(version: "1.0.0.0")).Relation);

    [Fact]
    public void SameVersion_IsNotPromotedToUpdate() =>
        Assert.Equal(DriverCandidateAssessment.VersionRelation.Same,
            DriverCandidateAssessment.Evaluate(Device(), Update(version: "2.0.0.0")).Relation);

    [Fact]
    public void CompatibleId_IsExplicitlyWeakerEvidence()
    {
        var result = DriverCandidateAssessment.Evaluate(Device(),
            Update(id: @"PCI\VEN_8086&DEV_1234"));
        Assert.Equal(1, result.HardwareEvidence);
        Assert.Contains("più generica", result.Summary);
    }

    [Theory]
    [InlineData("3.0-beta")]
    [InlineData("3")]
    [InlineData("3.0.0.0.1")]
    [InlineData("")]
    public void UnsupportedVersionStrings_AreNotComparable(string version) =>
        Assert.Equal(DriverCandidateAssessment.VersionRelation.NotComparable,
            DriverCandidateAssessment.Evaluate(Device(), Update(version: version)).Relation);

    [Fact]
    public void DifferentProvider_DoesNotCompareNumbers() =>
        Assert.Equal(DriverCandidateAssessment.VersionRelation.NotComparable,
            DriverCandidateAssessment.Evaluate(Device(), Update(provider: "Microsoft")).Relation);

    [Fact]
    public void MissingInstalledProvider_DoesNotCompareNumbers() =>
        Assert.Equal(DriverCandidateAssessment.VersionRelation.NotComparable,
            DriverCandidateAssessment.Evaluate(Device(provider: null), Update()).Relation);

    [Fact]
    public void UnrelatedDeviceId_BlocksVersionComparison()
    {
        var result = DriverCandidateAssessment.Evaluate(Device(),
            Update(id: @"PCI\VEN_10DE&DEV_9999"));
        Assert.Equal(0, result.HardwareEvidence);
        Assert.Equal(DriverCandidateAssessment.VersionRelation.NotComparable, result.Relation);
    }
}
