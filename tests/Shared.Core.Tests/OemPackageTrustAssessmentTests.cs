using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemPackageTrustAssessmentTests
{
    [Fact]
    public void MissingInputsNeverVerifyPackage()
    {
        var result = OemPackageTrustAssessment.Assess(null, null, null);
        Assert.Equal(OemPackageTrustAssessment.Risk.IncompleteEvidence, result.Result);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsPackageVerified);
    }

    [Fact]
    public void SignedCatalogWithoutMembershipRemainsIncomplete()
    {
        var signature = new CatalogTrustReport.Result(
            CatalogTrustReport.Status.ValidCatalogSignature, "Valid", "OEM");
        var result = OemPackageTrustAssessment.Assess(signature, null, null);
        Assert.Equal(OemPackageTrustAssessment.Risk.IncompleteEvidence, result.Result);
        Assert.True(result.CatalogSignatureValid);
    }

    [Fact]
    public void RejectedFileOverridesOtherPositiveEvidence()
    {
        var signature = new CatalogTrustReport.Result(
            CatalogTrustReport.Status.ValidCatalogSignature, "Valid", "OEM");
        var summary = new DriverPayloadAuditPolicy.Summary(1, 1, 0, 0);
        var result = OemPackageTrustAssessment.Assess(signature, summary, null);
        Assert.Equal(OemPackageTrustAssessment.Risk.Rejected, result.Result);
        Assert.False(result.IsInstallable);
    }

    [Fact]
    public void EvenPositivePartialEvidenceNeverPermitsInstallation()
    {
        var signature = new CatalogTrustReport.Result(
            CatalogTrustReport.Status.ValidCatalogSignature, "Valid", "OEM");
        var summary = new DriverPayloadAuditPolicy.Summary(2, 0, 0, 0);
        var dependency = new InfDependencyPreview.Report([], []);
        var result = OemPackageTrustAssessment.Assess(signature, summary, dependency);
        Assert.Equal(OemPackageTrustAssessment.Risk.PartialEvidence, result.Result);
        Assert.False(result.IsPackageVerified);
        Assert.False(result.IsInstallable);
    }
}
