using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WindowsPrinterInfCoverageTests
{
    private const string UsbPrinter = @"USBPRINT\Hewlett-PackardHP_Co3115";
    private const string BluetoothPrinter = @"BTHPRINT\Example_Printer_0012";
    private const string WsdPrinter = @"WSDPRINT\Example_Model_3000";
    private const string Dot4Printer = @"DOT4PRT\Example_Model_3000";
    private const string ParallelPrinter = @"LPTENUM\Hewlett-PackardHP_Co3115";
    private const string FireWirePrinter = @"1394\Example_Printer_5678";

    private static string Inf(string hardware, string? compatible = null) =>
        "[Version]\n" +
        "Signature=\"$Windows NT$\"\n" +
        "Class=Printer\n" +
        "CatalogFile.NTamd64=printer.cat\n" +
        "DriverVer=10/04/2026,4.0.0.0\n" +
        "[Manufacturer]\n" +
        "%Vendor%=PrintModels,NTamd64\n" +
        "[PrintModels.NTamd64]\n" +
        "%Model%=Install," + hardware +
        (compatible is null ? "" : "," + compatible) + "\n";

    private static DriverInfo Device(string hardware, string instance = "PRIVATE_INSTANCE") =>
        new(instance, "Windows printer device", "Generic manufacturer",
            "Printer", "Generic provider", "3.0.0.0",
            null, "oem-private.inf", true, true, 0, [hardware], []);

    private static MemoryStream Zip(params (string Path, string Contents)[] entries)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in entries)
            {
                var item = zip.CreateEntry(path, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(item.Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        memory.Position = 0;
        return memory;
    }

    [Theory]
    [InlineData(UsbPrinter, "USBPRINT")]
    [InlineData(ParallelPrinter, "LPTENUM")]
    [InlineData(Dot4Printer, "DOT4PRT")]
    [InlineData(BluetoothPrinter, "BTHPRINT")]
    [InlineData(WsdPrinter, "WSDPRINT")]
    [InlineData(FireWirePrinter, "1394")]
    public void MicrosoftPrinterEnumeratorInInfIsRecognizedWithExactHardwareIdentity(
        string hardware, string bus)
    {
        var content = Inf(hardware);
        var declared = Assert.Single(InfModelEvidence.ReadDeclaredIds(content));
        Assert.Equal(hardware.ToUpperInvariant(), declared.HardwareId);
        Assert.True(declared.IsPrimaryHardwareId);

        var device = Device(hardware);
        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadMatchingIds(content, [device])));
        var preflight = InfPlatformPreflight.Assess(
            content, device, "amd64", 10, 0, 26100, false);
        Assert.Equal(InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
            preflight.Result);
        Assert.Equal(2, preflight.HardwareMatchStrength);
        Assert.Contains(bus,
            HardwareIdentityParser.Describe(hardware), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stampa", HardwareIdentityParser.Describe(hardware));
    }

    [Fact]
    public void UsbBluetoothAndWsdPrintersAreMatchedFromBoundedLocalZip()
    {
        using var archive = Zip(
            ("usb/driver.inf", Inf(UsbPrinter)),
            ("bluetooth/driver.inf", Inf(BluetoothPrinter)),
            ("wsd/driver.inf", Inf(WsdPrinter)));
        var audit = OemZipInfEvidence.Inspect(archive);
        Assert.Equal(3, audit.InfWithDeclaredIds);
        Assert.Equal(3, audit.ReadableInfCount);

        archive.Position = 0;
        var report = LocalOemDriverComparison.Compare(
            archive,
            [
                Device(UsbPrinter, "PRIVATE_USB_INSTANCE"),
                Device(BluetoothPrinter, "PRIVATE_BLUETOOTH_INSTANCE"),
                Device(WsdPrinter, "PRIVATE_WSD_INSTANCE")
            ], "AMD64", 10, 0, 26100);

        Assert.Equal(3, report.InfExamined);
        Assert.Equal(3, report.DevicesWithEvidence);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.All(report.Findings, finding =>
        {
            Assert.True(finding.HasExactHardwareId);
            Assert.True(finding.IsPotentialNewer);
        });
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
        Assert.DoesNotContain("PRIVATE_", report.Summary);
    }

    [Fact]
    public void ExtractedPrinterDriverFolderUsesSameStrictIdsAndRemainsReadOnly()
    {
        var folder = Directory.CreateTempSubdirectory("OmegaDrive-PrinterInf-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "usb.inf"),
                Inf(UsbPrinter));
            File.WriteAllText(Path.Combine(folder.FullName, "dot4.inf"),
                Inf(Dot4Printer));

            var report = LocalOemFolderComparison.Compare(
                folder.FullName,
                [Device(UsbPrinter, "PRIVATE_USB"),
                 Device(Dot4Printer, "PRIVATE_DOT4")],
                "x64", 10, 0, 26100);
            Assert.Equal(2, report.InfExamined);
            Assert.Equal(2, report.TotalMatchedDevices);
            Assert.Equal(2, report.PotentialNewerDevices);
            Assert.False(report.IsInstallable);
            Assert.False(report.IsVerifiedCompatible);
            Assert.DoesNotContain(folder.FullName, report.Summary);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void CompatiblePrinterAliasRemainsWeakerEvenWithNewerInfVersion()
    {
        var inf = Inf(@"USBPRINT\Different_Printer_9999", UsbPrinter);
        var declared = InfModelEvidence.ReadMatchingIds(inf, [Device(UsbPrinter)]);
        Assert.Single(declared);
        Assert.False(declared[0].IsPrimaryHardwareId);
        Assert.Equal(1, InfModelEvidence.MatchStrength(Device(UsbPrinter), declared));

        using var archive = Zip(("printer.inf", inf));
        var report = LocalOemDriverComparison.Compare(
            archive, [Device(UsbPrinter)], "AMD64", 10, 0, 26100);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(1, finding.HardwareMatchStrength);
        Assert.False(finding.HasExactHardwareId);
        Assert.False(finding.IsPotentialNewer);
        Assert.Equal(0, report.PotentialNewerDevices);
        Assert.False(report.IsInstallable);
    }

    [Theory]
    [InlineData(@"USBPRINT\Hewlett-PackardHP_Co3116")]
    [InlineData(@"USBPRINT\HP_Co3115\PRIVATE_INSTANCE")]
    [InlineData(@"USBPRINT\..\driver.sys")]
    [InlineData(@"USBPRINT\HP_bad/driver")]
    [InlineData(@"BTHPRINT\HP with spaces")]
    [InlineData(@"WSDPRINT\Printer.exe")]
    [InlineData(@"LPTENUM\Printer?exec")]
    [InlineData(@"1394\Printer" + "\u202E")]
    public void NearMissInstancePathControlAndUntrustedIdsAreNotPromoted(
        string untrusted)
    {
        var inf = Inf(untrusted);
        var target = Device(UsbPrinter);
        var matched = InfModelEvidence.ReadMatchingIds(inf, [target]);
        // Near-miss ID is valid but NOT equal to the installed printer.
        // All other forms are invalid ID syntax and cannot match either.
        Assert.Empty(matched);
        Assert.Equal(0, InfModelEvidence.MatchStrength(target,
            InfModelEvidence.ReadDeclaredIds(inf)));
    }

    [Fact]
    public void UnknownPrinterLikeBusIsNotAcceptedSolelyByName()
    {
        const string unrecognized = @"FAKEPRINT\Example_Model_3000";
        Assert.Empty(InfModelEvidence.ReadDeclaredIds(Inf(unrecognized)));
        Assert.Equal(0, InfModelEvidence.MatchStrength(
            Device(unrecognized), InfModelEvidence.ReadDeclaredIds(Inf(unrecognized))));
    }
}
