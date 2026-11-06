using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WmiOptionalMetadataQueryTests
{
    [Fact]
    public void SuccessfulSignedDriverQueryKeepsWholeSnapshot()
    {
        var data = new Dictionary<string, string>
        {
            ["device-01"] = "31.0",
            ["device-02"] = "32.0"
        };
        var result = WmiOptionalMetadataQuery.Read(
            () => (IReadOnlyDictionary<string, string>)data,
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());

        Assert.True(result.Completed);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("32.0", result.Value["device-02"]);
    }

    [Fact]
    public void SuccessfulButEmptyMetadataQueryIsNotEvidenceOfMissingDrivers()
    {
        var result = WmiOptionalMetadataQuery.Read(
            () => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(),
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());

        Assert.True(result.Completed);
        Assert.False(WmiOptionalMetadataQuery.HasUsableRows(result));

        var populated = WmiOptionalMetadataQuery.Read(
            () => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["PnPDevice"] = "32.0"
            },
            (IReadOnlyDictionary<string, string>)new Dictionary<string, string>());
        Assert.True(WmiOptionalMetadataQuery.HasUsableRows(populated));
    }

    [Theory]
    [InlineData("management")]
    [InlineData("com")]
    [InlineData("access")]
    [InlineData("security")]
    public void RecoverableFailureDropsPartialSignedMetadata(string error)
    {
        var partiallyPopulated = new Dictionary<string, string>
        {
            ["PRIVATE_DEVICE_INSTANCE"] = "32.0"
        };
        var empty = new Dictionary<string, string>();
        var result = WmiOptionalMetadataQuery.Read<IReadOnlyDictionary<string, string>>(
            () =>
            {
                throw error switch
                {
                    "management" => new ManagementException("private serial"),
                    "com" => new COMException("private hardware id"),
                    "access" => new UnauthorizedAccessException("private info"),
                    _ => new SecurityException("private device")
                };
            }, empty);

        Assert.False(result.Completed);
        Assert.Empty(result.Value);
        Assert.DoesNotContain("PRIVATE_DEVICE_INSTANCE", result.Value.Keys);
        Assert.Single(partiallyPopulated);
    }

    [Fact]
    public void CancellationCannotBeSuppressedByOptionalSignedDriverFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            WmiOptionalMetadataQuery.Read(
                () =>
                {
                    cts.Token.ThrowIfCancellationRequested();
                    return "unreachable";
                }, string.Empty));
    }

    [Fact]
    public void UnexpectedProgrammingErrorsAreNotSilentlyHidden()
    {
        Assert.Throws<InvalidOperationException>(() =>
            WmiOptionalMetadataQuery.Read(
                () => throw new InvalidOperationException("bug"),
                "empty"));
    }
}
