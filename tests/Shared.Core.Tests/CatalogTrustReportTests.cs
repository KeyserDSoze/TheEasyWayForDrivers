using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class CatalogTrustReportTests
{
    [Fact]
    public void WindowsValidStatusDoesNotProveInfBelongsToCatalog()
    {
        var result = CatalogTrustReport.Parse(
            """{"Status":"Valid","Signer":"CN=Example Vendor"}""");
        Assert.Equal(CatalogTrustReport.Status.ValidCatalogSignature,
            result.SignatureStatus);
        Assert.Contains("NON verificata", result.Explanation);
        Assert.Equal("CN=Example Vendor", result.Signer);
    }

    [Theory]
    [InlineData("NotSigned")]
    [InlineData("NotTrusted")]
    [InlineData("HashMismatch")]
    public void InvalidSignaturesAreNotApproved(string status)
    {
        var result = CatalogTrustReport.Parse(
            $$$"""{"Status":"{{{status}}}","Signer":""}""");
        Assert.Equal(CatalogTrustReport.Status.InvalidSignature, result.SignatureStatus);
    }

    [Theory]
    [InlineData("UnknownError")]
    [InlineData("NotSupportedFileFormat")]
    public void UnknownWindowsStatusesRemainUnverified(string status)
    {
        var result = CatalogTrustReport.Parse(
            $$$"""{"Status":"{{{status}}}"}""");
        Assert.Equal(CatalogTrustReport.Status.NotVerified, result.SignatureStatus);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("""{"Status":0}""")]
    [InlineData("""{"Status":"Valid;InstallDriver"}""")]
    public void RejectsMalformedResponses(string json) =>
        Assert.ThrowsAny<Exception>(() => CatalogTrustReport.Parse(json));

    [Fact]
    public void SignerControlCharactersAreRemoved()
    {
        var result = CatalogTrustReport.Parse(
            """{"Status":"Valid","Signer":"CN=Vendor\r\nFake: trusted"}""");
        Assert.DoesNotContain('\r', result.Signer);
        Assert.DoesNotContain('\n', result.Signer);
    }
}
