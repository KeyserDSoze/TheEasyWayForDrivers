using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsDriverStoreEvidenceTests
{
    private static DriverInfo Device(
        string id = @"PCI\VEN_10DE&DEV_28A0\4&SECRET-SERIAL&0&0008") =>
        new(id, "GeForce RTX 4060 Laptop GPU", "NVIDIA", "Display",
            "NVIDIA", "32.0.15.5000", null, "oem14.inf", true,
            true, 0, [@"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA"],
            [@"PCI\VEN_10DE&DEV_28A0"]);

    [Fact]
    public void OnlyPnPReadOnlyPerDeviceCommandIsAllowed()
    {
        var device = Device();
        var args = WindowsDriverStoreEvidence.CommandArguments(device);
        Assert.Equal(
            ["/enum-devices", "/instanceid", device.DeviceId, "/drivers"], args);
        Assert.DoesNotContain(args, x => x.Equals(
            "/install", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, x => x.Equals(
            "/scan-devices", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, x => x.Equals(
            "/add-driver", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("PCI\\VEN_8086\n/disable-device")]
    [InlineData("PCI\\VEN_8086\r\n")]
    [InlineData("PCI\\VEN_8086\"")]
    [InlineData("PCI\\VEN_8086'")]
    public void RejectsInvalidOrInjectedInstanceIds(string instanceId) =>
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(Device(instanceId)));

    [Fact]
    public void RejectsExtremelyLongInstanceIds() =>
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(
                Device(new string('x', 513))));

    [Fact]
    public void WindowsEvidencePreservesActualRankTextButNeverClaimsAnUpdate()
    {
        var device = Device();
        const string report =
            "Instance ID: PCI\\VEN_10DE&DEV_28A0\\4&SECRET-SERIAL&0&0008\n" +
            "Matching Drivers:\n" +
            "  oem14.inf  Rank: 0x00FF2001  Date: 2026-10-04\n" +
            "  oem27.inf  Rank: 0x00FF0001  Date: 2025-08-19\n";

        var result = WindowsDriverStoreEvidence.FromCommandResult(0, report, device);

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, result.Status);
        Assert.Contains("0x00FF2001", result.DisplayText);
        Assert.Contains("0x00FF0001", result.DisplayText);
        Assert.Contains("oem27.inf", result.DisplayText);
        Assert.DoesNotContain("SECRET-SERIAL", result.DisplayText);
        Assert.Contains("[riga con ID dispositivo oscurata]", result.DisplayText);
        Assert.Contains("NON nuovi aggiornamenti online", result.DisplayText);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsNewDriverUpdate);
        Assert.False(result.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void RedactsHardwareAndCompatibleIdsAndUnsafeControlCharacters()
    {
        var device = Device();
        var result = WindowsDriverStoreEvidence.FromCommandResult(0,
            "ID: " + device.HardwareIds[0] + "\nCompatible: " +
            device.CompatibleIds[0] + "\nRank 0x00000001\u202E\0",
            device);

        Assert.DoesNotContain(device.HardwareIds[0], result.DisplayText);
        Assert.DoesNotContain(device.CompatibleIds[0], result.DisplayText);
        Assert.DoesNotContain('\0', result.DisplayText);
        Assert.DoesNotContain('\u202E', result.DisplayText);
        Assert.Contains("Rank 0x00000001", result.DisplayText);
    }


    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_0AA1\4&PRIVATE_NVIDIA_SERIAL")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B\PRIVATE_AUDIO")]
    [InlineData(@"USB\VID_8087&PID_0033\PRIVATE_BLUETOOTH")]
    [InlineData(@"ACPI\NVDA0820\PRIVATE_PLATFORM")]
    [InlineData(@"SWD\MMDEVAPI\{00000000-0000-4000-8000-1234567890AB}")]
    [InlineData(@"ROOT\UNKNOWN_DEV\PRIVATE_ROOT")]
    [InlineData(@"VIRTUALBUS\MODEL_0001\PRIVATE_OTHERVENDOR")]
    public void UnlistedPnpIdentitiesAreRedactedWithoutLosingRanks(
        string unknownIdentity)
    {
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, "Other device: " + unknownIdentity + "\n" +
                "oem27.inf  Rank: 0x00FF0001  Date: 2026-10-04\n",
            Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.Available, report.Status);
        Assert.DoesNotContain(unknownIdentity, report.DisplayText);
        Assert.Contains("[riga con ID dispositivo oscurata]", report.DisplayText);
        Assert.Contains("oem27.inf", report.DisplayText);
        Assert.Contains("0x00FF0001", report.DisplayText);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
    }

    [Fact]
    public void UnlistedPathsAndUrlsDoNotRevealLocalOrderIdentifiers()
    {
        var output =
            @"Source: C:\Users\PRIVATE_USER\Downloads\ORDER_928\driver.inf" + "\n" +
            @"Server: \\PRIVATE_PC\orders\PRIVATE_ORDER" + "\n" +
            "Info: https://vendor.example/private/PRIVATE_TOKEN\n" +
            "oem14.inf  Rank: 0x00FF2001  Version: 32.0.15.5000\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, output, Device());
        Assert.DoesNotContain("PRIVATE_USER", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_ORDER", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_PC", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_TOKEN", report.DisplayText);
        Assert.Contains("[percorso locale oscurato]", report.DisplayText);
        Assert.Contains("[indirizzo oscurato]", report.DisplayText);
        Assert.Contains("0x00FF2001", report.DisplayText);
        Assert.Contains("32.0.15.5000", report.DisplayText);
        Assert.Contains("oem14.inf", report.DisplayText);
        Assert.False(report.IsNewDriverUpdate);
    }

    [Fact]
    public void UnknownPnpIdsCannotLeakFromItalianLocalizedLines()
    {
        var output = "Driver corrispondenti:\n" +
            @"ID dispositivo: PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56" + "\n" +
            @"ID dispositivo: USB\VID_0BDA&PID_5655\PRIVATE_SERIAL" + "\n" +
            "Classificazione: 0x00001001\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, output, Device());
        Assert.DoesNotContain("VEN_8086", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_SERIAL", report.DisplayText);
        Assert.Contains("Classificazione: 0x00001001", report.DisplayText);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Theory]
    [InlineData(-1, "No driver")]
    [InlineData(1, "Access denied")]
    [InlineData(0, "")]
    [InlineData(0, " \r\n \t")]
    public void FailuresAndEmptyResponsesNeverMeanNoUpdates(
        int exitCode, string stdout)
    {
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            exitCode, stdout, Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified, result.Status);
        Assert.Contains("non significa che manchino driver o aggiornamenti",
            result.DisplayText);
        Assert.False(result.IsNewDriverUpdate);
    }

    [Fact]
    public void ExcessiveWindowsOutputIsRejectedNotTruncatedIntoFalseEvidence()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.FromCommandResult(0,
                new string('A', WindowsDriverStoreEvidence.MaxOutputCharacters + 1),
                Device()));
    }

    [Fact]
    public void DoesNotParseLocalizedRankOrTreatCachedPackagesAsUpdates()
    {
        var output =
            "Driver corrispondenti:\n" +
            "Nome INF pubblicato: oem11.inf\n" +
            "Classificazione: 0x00FF0000\n" +
            "Versione driver: 32.0.15.5000\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(0,
            output, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, result.Status);
        Assert.Contains("Classificazione: 0x00FF0000", result.DisplayText);
        Assert.False(result.IsNewDriverUpdate);
        Assert.False(result.IsInstallable);
    }
}
