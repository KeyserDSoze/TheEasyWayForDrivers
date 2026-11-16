using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #522: native PnPUtil and SetupAPI results are read-only LOCAL
/// diagnostics, not trusted text or evidence of a downloadable driver.
/// Redact standalone order/customer/serial markers even without PRIVATE_.
/// </summary>
public sealed class LocalDriverRankPrivateMarkerTests
{
    private static DriverInfo Device() =>
        new(@"PCI\VEN_10DE&DEV_28A0\4&1234&0&0008",
            "NVIDIA GeForce RTX 4060 Laptop GPU", "NVIDIA",
            "Display", "NVIDIA", "31.0.0.1", null,
            "oem10.inf", true, true, 0,
            [@"PCI\VEN_10DE&DEV_28A0"], []);

    [Theory]
    [InlineData("ORDER_ABC123")]
    [InlineData("SECRET_COMPANY")]
    [InlineData("SERIAL_MACHINE_042")]
    [InlineData("CUSTOMER_NAME_042")]
    [InlineData("order_abc123")]
    [InlineData("customer_name_042")]
    public void PrivatePnputilLineIsRedactedWithoutLosingIndependentPublicInf(
        string secret)
    {
        var stdout = "Published driver: " + secret +
            ".inf Rank: 0x00FF2001 Version: 99.99.99.99\n" +
            "Published driver: oem27.inf Rank: 0x00FF0001\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, stdout, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, report.Status);
        Assert.Contains("[riga con dati riservati oscurata]",
            report.DisplayText);
        Assert.Contains("oem27.inf Rank: 0x00FF0001", report.DisplayText);
        Assert.DoesNotContain(secret, report.DisplayText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("99.99.99.99", report.DisplayText);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Theory]
    [InlineData("ORDER_ABC123")]
    [InlineData("SECRET_COMPANY")]
    [InlineData("SERIAL_MACHINE_042")]
    [InlineData("CUSTOMER_NAME_042")]
    public void PrivateInfNameAloneCannotEstablishDriverStoreAvailable(
        string secret)
    {
        var stdout = "Matching Drivers:\n" +
            "Original INF name: " + secret + ".inf\n" +
            "Rank: 0x00001001\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, stdout, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.NotVerified, report.Status);
        Assert.Contains("non significa che manchino driver o aggiornamenti",
            report.DisplayText);
        Assert.DoesNotContain(secret, report.DisplayText);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void PrivateMarkerSplitByInvisibleUnicodeIsRedactedWholeLine()
    {
        var output = "Customer\u200B_NAME_042.inf Rank: 0x00FF0001\n" +
            "oem14.inf Rank: 0x00FF0002\n";
        var report = WindowsDriverStoreEvidence.FromCommandResult(
            0, output, Device());

        Assert.Equal(WindowsDriverStoreEvidence.State.Available, report.Status);
        Assert.DoesNotContain("Customer", report.DisplayText);
        Assert.DoesNotContain("NAME_042", report.DisplayText);
        Assert.DoesNotContain('\u200B', report.DisplayText);
        Assert.Contains("oem14.inf Rank: 0x00FF0002", report.DisplayText);
        Assert.False(report.IsInstallable);
    }

    [Theory]
    [InlineData("SERIAL_MACHINE_042")]
    [InlineData("CUSTOMER_NAME_042")]
    [InlineData("serial_machine_042")]
    [InlineData("customer_name_042")]
    public void NativeSetupApiDriverLabelsAreNotPublicNames(string sensitive)
    {
        Assert.Equal("[etichetta oscurata]",
            WindowsSetupApiRankEvidence.SafeLabel(
                "NVIDIA driver " + sensitive));

        var report = WindowsSetupApiRankEvidence.Summarize(
            [new WindowsSetupApiRankEvidence.Driver(
                0x00FF0001, "GeForce RTX " + sensitive,
                "Provider " + sensitive, 0x00200000000F0001UL)], true);

        Assert.Equal(WindowsSetupApiRankEvidence.State.Available,
            report.Status);
        Assert.Contains("Rank 0x00FF0001", report.DisplayText);
        Assert.Contains("32.0.15.1", report.DisplayText);
        Assert.Equal(2, report.DisplayText.Split(
            "[etichetta oscurata]").Length - 1);
        Assert.DoesNotContain(sensitive, report.DisplayText,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(report.IsInstallable);
        Assert.False(report.IsNewDriverUpdate);
        Assert.False(report.HasVerifiedExternalOemRanking);
    }

    [Fact]
    public void SecretPastNativeSetupApiDisplayCapIsNotShown()
    {
        var label = new string('A',
            WindowsSetupApiRankEvidence.MaxLabelCharacters + 10) +
            "_CUSTOMER_NAME_ABC123";
        Assert.Equal("[etichetta oscurata]",
            WindowsSetupApiRankEvidence.SafeLabel(label));
    }

    [Fact]
    public void LegitimateLocalizedPnputilAndSetupApiEvidenceRemainsReadable()
    {
        var pnp = WindowsDriverStoreEvidence.FromCommandResult(0,
            "Driver corrispondenti:\n" +
            "Nome INF pubblicato: oem27.inf\n" +
            "Classificazione: 0x00FF0001\n", Device());
        Assert.Equal(WindowsDriverStoreEvidence.State.Available, pnp.Status);
        Assert.Contains("oem27.inf", pnp.DisplayText);
        Assert.Contains("Classificazione", pnp.DisplayText);

        var setup = WindowsSetupApiRankEvidence.Summarize(
            [new WindowsSetupApiRankEvidence.Driver(
                0x00FF0001, "NVIDIA GeForce RTX Laptop GPU",
                "Intel Killer Wi-Fi", 0x00200000000F0001UL)], true);
        Assert.Contains("NVIDIA GeForce RTX Laptop GPU", setup.DisplayText);
        Assert.Contains("Intel Killer Wi-Fi", setup.DisplayText);
        Assert.False(pnp.IsNewDriverUpdate);
        Assert.False(setup.IsNewDriverUpdate);
    }
}
