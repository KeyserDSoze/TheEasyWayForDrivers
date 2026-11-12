using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #407: PnPUtil output can contain unknown or selected instance
/// identifiers with arbitrary text on the same diagnostic line.
/// </summary>
public sealed class WindowsDriverStoreEvidenceLinePrivacyTests
{
    private static DriverInfo Device() =>
        new(@"PCI\VEN_10DE&DEV_28A0\KNOWN INSTANCE SERIAL",
            "NVIDIA GeForce RTX 4060 Laptop GPU", "NVIDIA",
            "Display", "NVIDIA", "32.0.15.5000",
            null, "oem14.inf", true, true, 0,
            [@"PCI\VEN_10DE&DEV_28A0"], []);

    [Theory]
    [InlineData(@"PCI\VEN_8086&DEV_51F1\EXTRA SERIAL 1234")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295\AUDIO NAME 2026")]
    [InlineData(@"USB\VID_8087&PID_0033\BLUETOOTH IDENTIFIER 42")]
    [InlineData(@"ACPI\NVDA0820\PLATFORM PRIVATE NAME")]
    [InlineData(@"SWD\MMDEVAPI\{00000000-0000-4000-8000-1234567890AB} USER")]
    [InlineData(@"ROOT\UNKNOWN_MODEL\SERIAL A, B + 1")]
    public void UnlistedIdentityWithSpacedSuffixRedactsTheWholeLine(string id)
    {
        var input = "Altro ID dispositivo: " + id +
            "\noem27.inf  Rank: 0x00FF0001  Version: 31.0.101.5590\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, input, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, report.Status);
        Assert.Contains("[riga con ID dispositivo oscurata]", report.DisplayText);
        Assert.DoesNotContain(id, report.DisplayText);
        Assert.DoesNotContain("Altro ID dispositivo:", report.DisplayText);
        Assert.Contains("oem27.inf", report.DisplayText);
        Assert.Contains("0x00FF0001", report.DisplayText);
        Assert.Contains("31.0.101.5590", report.DisplayText);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void PrivateTextBeforeAndAfterUnknownPnPIdIsAlsoRemoved()
    {
        const string reportLine =
            "User tag: PRIVATE_OWNER. Device: PCI\\VEN_14E4&DEV_43A0\\" +
            "HIDDEN SERIAL, note=PRIVATE_ORDER_CODE";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, reportLine + "\nClassificazione: 0x00FF2001\n", Device());

        Assert.DoesNotContain("PRIVATE_OWNER", report.DisplayText);
        Assert.DoesNotContain("HIDDEN SERIAL", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_ORDER_CODE", report.DisplayText);
        Assert.Contains("Classificazione: 0x00FF2001", report.DisplayText);
        Assert.Contains("[riga con ID dispositivo oscurata]", report.DisplayText);
    }

    [Fact]
    public void KnownInventoryIdWithAdjacentPrivateTextCannotLeakFragments()
    {
        var device = Device();
        var line = "PRIVATE_HEADER " + device.DeviceId +
            " REST OF PRIVATE SERIAL AFTER KNOWN ID";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, line + "\nRank: 0x00FF0001\noem14.inf\n", device);

        Assert.DoesNotContain("PRIVATE_HEADER", report.DisplayText);
        Assert.DoesNotContain("REST OF PRIVATE", report.DisplayText);
        Assert.DoesNotContain("KNOWN INSTANCE", report.DisplayText);
        Assert.Contains("0x00FF0001", report.DisplayText);
        Assert.Contains("oem14.inf", report.DisplayText);
    }

    [Fact]
    public void TwoIdentitiesOnOneLineAreRedactedTogether()
    {
        var line = @"PCI\VEN_10DE&DEV_28A0\GPU SERIAL / " +
            @"PCI\VEN_8086&DEV_51F1\WIFI SERIAL";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, line + "\nDriver version: 33.0.1\n", Device());

        Assert.DoesNotContain("GPU SERIAL", report.DisplayText);
        Assert.DoesNotContain("WIFI SERIAL", report.DisplayText);
        Assert.Equal(1, report.DisplayText.Split(
            "[riga con ID dispositivo oscurata]").Length - 1);
        Assert.Contains("Driver version: 33.0.1", report.DisplayText);
    }

    [Fact]
    public void AlreadyRedactedLocalPathsAndUrlsStillStayPrivate()
    {
        const string output =
            "File: C:\\Users\\PRIVATE_USER\\driver.inf\n" +
            "Site: https://private.example/path/PRIVATE_TOKEN\n" +
            "oem10.inf Rank: 0x00FF0001";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, output, Device());

        Assert.DoesNotContain("PRIVATE_USER", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_TOKEN", report.DisplayText);
        Assert.Contains("oem10.inf", report.DisplayText);
        Assert.Contains("0x00FF0001", report.DisplayText);
    }

    [Fact]
    public void PureRankingLinesAreNotRemovedWithoutPnPIdentifiers()
    {
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0,
            "Driver corrispondenti:\n" +
            "Nome INF pubblicato: oem11.inf\n" +
            "Classificazione: 0x00FF0000\n" +
            "Versione driver: 32.0.15.5000\n", Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, report.Status);
        Assert.Contains("oem11.inf", report.DisplayText);
        Assert.Contains("Classificazione: 0x00FF0000", report.DisplayText);
        Assert.Contains("Versione driver: 32.0.15.5000", report.DisplayText);
        Assert.DoesNotContain("[riga con ID dispositivo oscurata]",
            report.DisplayText);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void FailedPnPUtilCommandNeverBecomesRankEvidence()
    {
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            1, @"PCI\VEN_10DE&DEV_28A0\PRIVATE SERIAL", Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified, report.Status);
        Assert.False(report.IsInstallable);
        Assert.DoesNotContain("PRIVATE", report.DisplayText);
    }
}
