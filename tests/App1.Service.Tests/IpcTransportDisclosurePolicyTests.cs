using System.IO;
using System.Text.Json;
using TheEasyWayForDrivers.ServiceApp.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// #550: transport error frames/log labels must not echo a privileged
/// provider exception or an attacker-controlled command. All simulated.
/// </summary>
public sealed class IpcTransportDisclosurePolicyTests
{
    [Theory]
    [InlineData("scan")]
    [InlineData("search-updates")]
    [InlineData("search-report")]
    [InlineData("install-updates")]
    [InlineData("oem-providers")]
    [InlineData("check-app-update")]
    [InlineData("diagnostics")]
    public void OnlyKnownExactServiceCommandNamesMayAppearInLogs(
        string command)
    {
        Assert.Equal(command, IpcTransportDisclosurePolicy.SafeCommandLabel(command));
    }

    [Theory]
    [InlineData("SCAN")]
    [InlineData("invalid")]
    [InlineData("C:\\Users\\PRIVATE_CUSTOMER\\drivers.inf")]
    [InlineData("diagnostics\nPRIVATE_SERIAL")]
    [InlineData("search-report \t")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownAndPrivateCommandNamesAreNotReflected(string? command)
    {
        var label = IpcTransportDisclosurePolicy.SafeCommandLabel(command);
        Assert.Equal("comando non riconosciuto", label);
        Assert.DoesNotContain("PRIVATE_", label);
        Assert.DoesNotContain("Users", label);
        Assert.DoesNotContain("SERIAL", label);
    }

    [Fact]
    public void JsonAndInvalidArgumentsProduceGenericValidationError()
    {
        var errors = new Exception[]
        {
            new JsonException("JSON at C:\\Users\\PRIVATE_CUSTOMER\\config.json"),
            new ArgumentException(
                @"Bad device PCI\VEN_10DE&DEV_28A0 at C:\Users\PRIVATE_CUSTOMER")
        };
        foreach (var error in errors)
            Assert.Equal(IpcTransportDisclosurePolicy.InvalidRequest,
                IpcTransportDisclosurePolicy.ToClientError(error));
    }

    [Fact]
    public void ProviderFailuresNeverEchoPrivatePathsOrEndpoints()
    {
        var errors = new Exception[]
        {
            new IOException(@"C:\Users\PRIVATE_CUSTOMER\drivers\local.inf"),
            new UnauthorizedAccessException(
                @"C:\ProgramData\PRIVATE_SERIAL\driver"),
            new InvalidOperationException(
                @"WSUS server https://PRIVATE_HOSTNAME\driver"),
            new NotSupportedException("OEM serial PRIVATE_ORDER_12345"),
            new OperationCanceledException("PRIVATE_DEVICE"),
            new AggregateException(new IOException("PRIVATE_USERNAME"))
        };
        foreach (var error in errors)
        {
            var safe = IpcTransportDisclosurePolicy.ToClientError(error);
            Assert.Equal(IpcTransportDisclosurePolicy.OperationFailed, safe);
            Assert.DoesNotContain("PRIVATE_", safe);
            Assert.DoesNotContain("Users", safe);
            Assert.DoesNotContain("WSUS", safe);
        }
    }

    [Fact]
    public void NoErrorCanUseRawExceptionMessageAsClientResponse()
    {
        const string privateValue =
            @"PRIVATE_USERNAME: PCI\VEN_10DE&DEV_28A0 C:\Users\secret";
        var badInput = new ArgumentException(privateValue);
        var failedProvider = new InvalidOperationException(privateValue);
        Assert.DoesNotContain(privateValue,
            IpcTransportDisclosurePolicy.ToClientError(badInput));
        Assert.DoesNotContain(privateValue,
            IpcTransportDisclosurePolicy.ToClientError(failedProvider));
    }
}
