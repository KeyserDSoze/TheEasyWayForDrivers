using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #409: the local descriptive hint must never print raw PnP instance
/// serials or unvalidated "public" model strings from legacy IPC/WMI.
/// </summary>
public sealed class HardwareIdentityHintPrivacyTests
{
    [Theory]
    [InlineData(@"ROOT\PRIVATE_SERIAL_12345", "virtuale")]
    [InlineData(@"SWD\MMDEVAPI_PRIVATE_PERSON_42", "virtuale")]
    [InlineData(@"USBSTOR\DISK&VEN_PRIVATE&PROD_PRIVATE", "archiviazione")]
    [InlineData(@"BTHENUM\DEV_PRIVATE_TOKEN", "Bluetooth")]
    [InlineData(@"HID\VID_046D&PID_C534&PRIVATE_UID_2026", "HID")]
    [InlineData(@"MONITOR\PRIVATE_SERIAL_2026", "monitor")]
    [InlineData(@"USBPRINT\PRIVATE_PRINTER_ORDER_123", "stampa")]
    [InlineData(@"VMBUS\{00112233-4455-6677-8899-AABBCCDDEEFF}", "Hyper-V")]
    [InlineData(@"ACPI\PRIVATE_SERIAL_2026", "ACPI")]
    public void GenericBusOnlyReportsCategoryNotRawIdentifier(
        string hardware, string category)
    {
        var output = HardwareIdentityParser.Describe(hardware);
        Assert.Contains(category, output);
        Assert.DoesNotContain("PRIVATE", output);
        Assert.DoesNotContain("SERIAL", output);
        Assert.DoesNotContain("ORDER", output);
        Assert.DoesNotContain("MMDEVAPI", output);
        Assert.DoesNotContain("00112233", output);
        Assert.DoesNotContain("VID_046D", output);
        Assert.Contains("senza dedurne il produttore", output);
        Assert.DoesNotContain("driver disponibile", output);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\4&PRIVATE_SERIAL")]
    [InlineData(@"USB\VID_046D&PID_C534\PRIVATE_SERIAL")]
    [InlineData(@"ACPI\PNP0C09\PRIVATE_INSTANCE")]
    [InlineData(@"ROOT\MEDIA\PRIVATE_SERIAL")]
    [InlineData(@"PCI\GARBAGE&VEN_10DE&DEV_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&PRIVATE_ID")]
    [InlineData(@"USB\GARBAGE&VID_046D&PID_C534")]
    [InlineData(@"USB\VID_046D&PID_C534&PRIVATE_SERIAL")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_NOTHEX")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0/PRIVATE")]
    [InlineData(@"ROOT\MEDIA\u202EPRIVATE")]
    [InlineData(@"USBPRINT\X:PRIVATE")]
    [InlineData(@"\\PRIVATE-SERVER\PRIVATE-SHARE")]
    [InlineData("https://private.example/driver")]
    public void InvalidInstanceOrForgedModelCannotProducePublicHint(
        string value)
    {
        var output = HardwareIdentityParser.Describe(value);
        Assert.Contains("non riconosciuto", output);
        Assert.DoesNotContain("PRIVATE", output);
        Assert.DoesNotContain("10DE", output);
        Assert.DoesNotContain("046D", output);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0&SUBSYS_161E1025",
        "Bus PCI", "10DE", "28A0", "161E1025")]
    [InlineData(@"pci\ven_8086&dev_51F1&rev_01",
        "Bus PCI", "8086", "51F1", "Verificare")]
    [InlineData(@"USB\VID_0BDA&PID_5655&MI_00",
        "Bus USB", "0BDA", "5655", "Verificare")]
    public void StrictPublicPciUsbModelsStillExplainVendorAndProduct(
        string id, string label, string vendor, string device, string extra)
    {
        var output = HardwareIdentityParser.Describe(id);
        Assert.Contains(label, output);
        Assert.Contains(vendor, output);
        Assert.Contains(device, output);
        Assert.Contains(extra, output);
        Assert.DoesNotContain("driver disponibile", output);
    }

    [Theory]
    [InlineData(@"ACPI\PNP0C09", "PNP0C09")]
    [InlineData(@"ACPI\NVDA0820", "NVDA0820")]
    public void KnownPublicAcpiModelRemainsInformative(
        string id, string publicCode)
    {
        var output = HardwareIdentityParser.Describe(id);
        Assert.Contains("ACPI", output);
        Assert.Contains(publicCode, output);
    }

    [Fact]
    public void OverlongOrControlBearingInputIsNotPrinted()
    {
        var output = HardwareIdentityParser.Describe(
            @"ROOT\" + new string('A', 700));
        Assert.Contains("non riconosciuto", output);
        Assert.DoesNotContain(new string('A', 20), output);
        var control = HardwareIdentityParser.Describe("ROOT\\PRIVATE\r\nSERIAL");
        Assert.Contains("non riconosciuto", control);
        Assert.DoesNotContain("PRIVATE", control);
    }

    [Fact]
    public void KnownHardwareIdNotEquatedWithOfferOrInstalledDriver()
    {
        var output = HardwareIdentityParser.Describe(
            @"PCI\VEN_10DE&DEV_28A0");
        Assert.Contains("Verificare il modello", output);
        Assert.DoesNotContain("aggiornamento disponibile", output);
    }
}
