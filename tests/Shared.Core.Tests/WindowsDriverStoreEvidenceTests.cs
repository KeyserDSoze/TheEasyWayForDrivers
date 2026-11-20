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
            device.CompatibleIds[0] + "\noem14.inf Rank 0x00000001\u202E\0",
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
        // Whole-line redaction can supersede path-only redaction when
        // a path also contains a PnP-like identity segment. Both redact
        // more conservatively than keeping a partial private suffix.
        Assert.Contains("[riga con ID dispositivo oscurata]", report.DisplayText);
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
            "Nome INF pubblicato: oem27.inf\n" +
            "Classificazione: 0x00001001\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, output, Device());
        Assert.DoesNotContain("VEN_8086", report.DisplayText);
        Assert.DoesNotContain("PRIVATE_SERIAL", report.DisplayText);
        Assert.Contains("Classificazione: 0x00001001", report.DisplayText);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Theory]
    [InlineData("Instance ID: PCI\\VEN_8086&DEV_51F1\\PRIVATE_SERIAL\n")]
    [InlineData("ID dispositivo: USB\\VID_0BDA&PID_5655\\PRIVATE_SERIAL\r\n")]
    [InlineData("Server: \\\\PRIVATE_PC\\PRIVATE_SHARE\r\n")]
    [InlineData("Web: https://vendor.example/PRIVATE_SECRET\n")]
    [InlineData(" \r\n Instance ID: PCI\\VEN_8086&DEV_51F1\\PRIVATE_SERIAL\r\n \r\n")]
    public void RedactedOnlySuccessfulPnputilOutputIsNotVerified(string stdout)
    {
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, stdout, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified,
            result.Status);
        Assert.Contains("non significa che manchino driver o aggiornamenti",
            result.DisplayText);
        Assert.DoesNotContain("PRIVATE_", result.DisplayText);
        Assert.DoesNotContain(@"PCI\", result.DisplayText);
        Assert.DoesNotContain(@"USB\", result.DisplayText);
        Assert.DoesNotContain("https://", result.DisplayText);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsNewDriverUpdate);
        Assert.False(result.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void MixedSensitiveLinesWithoutAnyDriverDetailsRemainUnverified()
    {
        var native = "Instance ID: " + Device().DeviceId + "\r\n" +
            @"Source: C:\Users\PRIVATE_USER\Downloads\driver.inf" + "\r\n" +
            @"Server: \\PRIVATE_PC\PRIVATE_SHARE" + "\r\n" +
            "URL: https://vendor.example/PRIVATE_TOKEN\r\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified,
            result.Status);
        Assert.DoesNotContain("PRIVATE", result.DisplayText);
        Assert.DoesNotContain("0 driver", result.DisplayText);
        Assert.False(result.IsNewDriverUpdate);
    }

    [Fact]
    public void OnePublicInfRankLineStillMakesSanitizedEvidenceAvailable()
    {
        var native = "Instance ID: " + Device().DeviceId + "\r\n" +
            @"Source: C:\Users\PRIVATE_USER\Downloads\driver.inf" + "\r\n" +
            "  oem27.inf   Rank: 0x00FF0001  Version: 32.0.15.5000\r\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available,
            result.Status);
        Assert.Contains("oem27.inf", result.DisplayText);
        Assert.Contains("0x00FF0001", result.DisplayText);
        Assert.DoesNotContain("PRIVATE", result.DisplayText);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsNewDriverUpdate);
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
    [Theory]
    [InlineData("Matching Drivers:\n")]
    [InlineData("Driver corrispondenti:\r\n")]
    [InlineData("Matching Drivers:\nRank: 0x00FF0001\n")]
    [InlineData("Driver corrispondenti:\nClassificazione: 0x00FF0001\n")]
    [InlineData("Matching Drivers:\nDriver name:\nDriver version:\n")]
    [InlineData("Info: oem14.inf.backup\n")]
    [InlineData("Info: preoem14.informazione\n")]
    [InlineData("Info: /home/PRIVATE_USER/oem14.inf\n")]
    [InlineData(@"Source: C:\Users\PRIVATE_ACCOUNT\oem14.inf")]
    [InlineData("URL: https://vendor.example/download/oem14.inf")]
    public void HeadingsOrRankWithoutActualDriverInfRemainUnverified(
        string native)
    {
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified,
            result.Status);
        Assert.Contains("nome INF driver verificabile", result.DisplayText);
        Assert.Contains("non significa che manchino driver o aggiornamenti",
            result.DisplayText);
        Assert.DoesNotContain("PRIVATE_", result.DisplayText);
        Assert.False(result.IsNewDriverUpdate);
        Assert.False(result.IsInstallable);
        Assert.False(result.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void OnlyPrivateDeviceIdAndHeadingsCannotCertifyLocalDrivers()
    {
        var device = Device();
        var native = "Matching Drivers:\n" +
            "Instance ID: " + device.DeviceId + "\n" +
            "Hardware: " + device.HardwareIds[0] + "\n" +
            "Rank: 0x00FF0001\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, device);

        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified,
            result.Status);
        Assert.DoesNotContain(device.DeviceId, result.DisplayText);
        Assert.DoesNotContain(device.HardwareIds[0], result.DisplayText);
        Assert.DoesNotContain("SECRET-SERIAL", result.DisplayText);
        Assert.DoesNotContain("0x00FF0001", result.DisplayText);
    }

    [Theory]
    [InlineData("oem14.inf")]
    [InlineData("netwtw6e.inf")]
    [InlineData("HDaUdBus.INF")]
    public void VisibleNativeInfRemainsReadOnlyEvidenceOnLocalizedOutput(
        string infName)
    {
        var native = "Driver corrispondenti:\r\n" +
            "Nome INF pubblicato: " + infName + "\r\n" +
            "Classificazione: 0x00FF0001\r\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available,
            result.Status);
        Assert.Contains(infName, result.DisplayText);
        Assert.Contains("0x00FF0001", result.DisplayText);
        Assert.Contains("NON nuovi aggiornamenti online", result.DisplayText);
        Assert.False(result.IsNewDriverUpdate);
        Assert.False(result.IsInstallable);
        Assert.False(result.HasVerifiedExternalOemRanking);
    }


    [Theory]
    [InlineData("owner@example.invalid")]
    [InlineData("Contact PRIVATE_CUSTOMER")]
    [InlineData("Adapter SUBSYS_16A217AA")]
    [InlineData("Hardware VID_8087&PID_0033")]
    [InlineData("Instance SerialNumber=ORDER12345")]
    [InlineData("Package /Users/customer/Library/secret")]
    [InlineData("Cache /home/customer/secret")]
    [InlineData("Cached /mnt/shared/secret")]
    [InlineData("owner@\u200Bexample.invalid")]
    [InlineData("PRI\u200BVATE_CUSTOMER")]
    [InlineData("VE\u200BN_10DE&DEV_28A0")]
    public void PrivateLineMarkersCannotBeShownNextToAnIndependentInf(
        string privateText)
    {
        var native = "Proprietario: " + privateText + "\n" +
            "Nome INF pubblicato: oem27.inf\n" +
            "Rank: 0x00FF2001\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, result.Status);
        Assert.DoesNotContain(privateText, result.DisplayText);
        Assert.DoesNotContain("CUSTOMER", result.DisplayText);
        Assert.DoesNotContain("owner@", result.DisplayText);
        Assert.DoesNotContain("SUBSYS_", result.DisplayText);
        Assert.DoesNotContain("SerialNumber", result.DisplayText);
        Assert.DoesNotContain("/Users/", result.DisplayText);
        Assert.DoesNotContain("/home/", result.DisplayText);
        Assert.DoesNotContain("/mnt/", result.DisplayText);
        Assert.Contains("oem27.inf", result.DisplayText);
        Assert.Contains("0x00FF2001", result.DisplayText);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsNewDriverUpdate);
    }

    [Theory]
    [InlineData("oem14.inf owner@example.invalid")]
    [InlineData("oem14.inf PRIVATE_ACCOUNT")]
    [InlineData("oem14.inf DEV_28A0")]
    [InlineData("oem14.inf /home/customer/order")]
    public void SoleInfOnPrivateLineCannotProveDriverStoreEvidence(
        string privateLine)
    {
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, "Driver corrispondenti:\n" + privateLine + "\n" +
                "Classificazione: 0x00FF0001", Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified, result.Status);
        Assert.Contains("non significa che manchino driver o aggiornamenti",
            result.DisplayText);
        Assert.DoesNotContain("oem14.inf", result.DisplayText);
        Assert.DoesNotContain("PRIVATE_", result.DisplayText);
        Assert.False(result.HasVerifiedExternalOemRanking);
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\SECRET\u200BSERIAL")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\SECRET\u2060SERIAL")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\SECRET\uFFFD")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\SECRET\u2028SERIAL")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\SECRET\u00A0SERIAL")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\SECRET\uD800")]
    public void InvisibleOrDamagedInstanceIdIsNeverPassedToPnputil(
        string deviceId)
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(Device(deviceId)));
    }

    [Fact]
    public void RedactedDiagnosticCannotRetainOtherInvisibleUnicode()
    {
        var native = "Nome INF pubblicato: oem14.inf\u200B\u2028\u2060\uFFFD\n" +
            "Rank: 0x00FF0001\u202E\n";
        var result = WindowsDriverStoreEvidence.FromCommandResult(
            0, native, Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.Available, result.Status);
        Assert.Contains("oem14.inf", result.DisplayText);
        Assert.Contains("0x00FF0001", result.DisplayText);
        foreach (var ch in new[] { '\u200B', '\u2028', '\u2060',
            '\uFFFD', '\u202E' })
            Assert.DoesNotContain(ch, result.DisplayText);
        Assert.False(result.IsInstallable);
    }


    [Theory]
    [InlineData("Pubblicato: oem27.inf.bak")]
    [InlineData("Pubblicato: preoem27.informazioni")]
    [InlineData("Pubblicato: /vendor/oem27.inf")]
    [InlineData("Pubblicato: oem27.inf/subdir")]
    [InlineData("Pubblicato: oem27.inf+suffix")]
    [InlineData("Pubblicato: prefix+oem27.inf")]
    [InlineData("Pubblicato: _oem27.inf")]
    [InlineData("Pubblicato: .inf")]
    public void IndependentInfScannerRejectsEmbeddedOrInvalidBasenames(
        string nativeLine)
    {
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, "Driver corrispondenti:\n" + nativeLine + "\n",
            Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified, report.Status);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void IndependentInfScannerAcceptsMaximumAsciiBasename()
    {
        var infName = new string('a', 255) + ".INF";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, "Nome INF pubblicato: " + infName + "\n" +
                "Classificazione: 0x00FF0001\n", Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.Available, report.Status);
        Assert.Contains(infName, report.DisplayText);
        Assert.False(report.IsNewDriverUpdate);
    }

    [Fact]
    public void IndependentInfScannerRejectsOverlongBasenameWithoutTimeout()
    {
        var huge = new string('a', 3000);
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, "Nome INF pubblicato: " + huge + ".inf\n", Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified, report.Status);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
    }

}
