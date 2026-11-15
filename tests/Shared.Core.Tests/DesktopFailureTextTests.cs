using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DesktopFailureTextTests
{
    [Fact]
    public void DirectFailureTextNeverContainsDetailsFromUntrustedExceptions()
    {
        const string secret = @"C:\Users\PRIVATE_NAME\Desktop\PCI\VEN_10DE&DEV_28A0";
        var exceptions = new Exception[]
        {
            new IOException(secret),
            new InvalidOperationException(secret),
            new UnauthorizedAccessException(secret),
            new InvalidDataException(secret),
            new JsonException(secret),
            new TimeoutException(secret),
            new OperationCanceledException(secret),
            new ArgumentException("owner@example.invalid " + secret)
        };
        foreach (var exception in exceptions)
        {
            var display = DesktopFailureText.BusyOperation(exception);
            Assert.NotEmpty(display);
            Assert.DoesNotContain("PRIVATE_", display);
            Assert.DoesNotContain("VEN_", display);
            Assert.DoesNotContain("C:\\", display);
            Assert.DoesNotContain("@", display);
            Assert.DoesNotContain("example.invalid", display);
            Assert.DoesNotContain(exception.Message, display);
        }
    }

    [Fact]
    public void BusyOperationExplainsFailureWithoutPretendingDriverScanCompleted()
    {
        Assert.Contains("interrotta", DesktopFailureText.BusyOperation(
            new OperationCanceledException("private")));
        Assert.Contains("Autorizzazioni", DesktopFailureText.BusyOperation(
            new UnauthorizedAccessException("private")));
        Assert.Contains("non leggibili", DesktopFailureText.BusyOperation(
            new InvalidDataException("private")));
        Assert.Contains("non completata", DesktopFailureText.BusyOperation(
            new InvalidOperationException("private")));
        Assert.Throws<ArgumentNullException>(() =>
            DesktopFailureText.BusyOperation(null!));
    }

    [Fact]
    public void FixedSettingsHistoryAndConnectionTextsArePrivacySafe()
    {
        foreach (var value in new[]
        {
            DesktopFailureText.ConnectionInterrupted,
            DesktopFailureText.SettingsSaveFailed,
            DesktopFailureText.SettingsResetFailed,
            DesktopFailureText.HistorySaveFailed,
            DesktopFailureText.HistoryReadFailed,
            DesktopFailureText.HistoryExportFailed
        })
        {
            Assert.NotEmpty(value);
            Assert.DoesNotContain("PRIVATE_", value);
            Assert.DoesNotContain("C:\\", value);
            Assert.DoesNotContain("VEN_", value);
            Assert.DoesNotContain("@", value);
            Assert.DoesNotContain(value, char.IsControl);
        }
    }
}
