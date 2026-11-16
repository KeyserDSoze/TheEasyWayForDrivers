using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #521: external OEM ZIP labels and WMI metadata must not leak private
/// PnP/serial/order data into copyable read-only comparison findings.
/// Hardware matching and OEM DriverVer comparison use original metadata.
/// </summary>
public sealed class OemZipFindingPrivacyBoundaryTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Killer = @"PCI\VEN_8086&DEV_51F1";
    private const string Realtek = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0295";

    private static MemoryStream Zip(
        params (string Name, string HardwareId, string Version)[] entries)
    {
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var (path, hardware, version) in entries)
            {
                var data = $"""
                    [Version]
                    Signature="$Windows NT$"
                    Class=Display
                    DriverVer=11/16/2026,{version}
                    [Manufacturer]
                    %Vendor%=Models,NTamd64.10.0
                    [Models.NTamd64.10.0]
                    %Model%=Install,{hardware}
                    """;
                using var writer = new StreamWriter(
                    zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(data);
            }
        }
        memory.Position = 0;
        return memory;
    }

    private static DriverInfo Device(
        string hardware, string name, string driverVersion = "31.0.0.1",
        string category = "Display")
    {
        var identity = hardware + @"\PRIVATE_INSTANCE_SERIAL_521";
        return new DriverInfo(identity, name, "Microsoft", category,
            "Microsoft", driverVersion, null, "oem10.inf",
            true, true, 0, [hardware], []);
    }

    private static LocalOemDriverComparison.Report Compare(
        MemoryStream zip, params DriverInfo[] devices) =>
        LocalOemDriverComparison.Compare(
            zip, devices, "x64", 10, 0, 26100);

    [Theory]
    [InlineData("PRIVATE_ORDER_521_GPU.inf")]
    [InlineData("serialnumber-ABCDEF-driver.inf")]
    [InlineData("pci_ven_10de-dev_28a0.inf")]
    [InlineData("SECRET_CUSTOMER_ID.inf")]
    public void PrivateZipInfBasenamesDoNotReachCopyableFinding(
        string basename)
    {
        using var zip = Zip(
            ("drivers/" + basename, Nvidia, "32.0.0.1"));
        var report = Compare(zip, Device(Nvidia, "NVIDIA RTX 4060"));
        var finding = Assert.Single(report.Findings);

        Assert.Equal("file INF OEM (nome riservato)", finding.InfName);
        Assert.Equal("NVIDIA RTX 4060", finding.DeviceName);
        Assert.Equal("31.0.0.1", finding.InstalledVersion);
        Assert.Equal("32.0.0.1", finding.PackageVersion);
        Assert.True(finding.HasExactHardwareId);
        Assert.True(finding.IsPotentialNewer);
        Assert.DoesNotContain("PRIVATE", finding.Summary);
        Assert.DoesNotContain("CUSTOMER", finding.Summary);
        Assert.DoesNotContain("ABCDEF", finding.Summary);
        Assert.DoesNotContain("ven_10de", finding.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(report.IsInstallable);
    }

    [Fact]
    public void LongInfWithSecretAfterNinetiethCharacterIsNeverTruncatedIntoUi()
    {
        var basename = new string('A', 91) +
            "_PRIVATE_ORDER_521_NVIDIA.inf";
        using var zip = Zip(
            ("drivers/" + basename, Nvidia, "32.0.0.1"));
        var row = Assert.Single(Compare(zip,
            Device(Nvidia, "NVIDIA GeForce RTX 4060")).Findings);

        Assert.Equal("file INF OEM (nome riservato)", row.InfName);
        Assert.DoesNotContain(new string('A', 30), row.InfName);
        Assert.DoesNotContain("PRIVATE_ORDER", row.Summary);
        Assert.True(row.HasExactHardwareId);
        Assert.Equal("32.0.0.1", row.PackageVersion);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\PRIVATE_INSTANCE")]
    [InlineData(@"C:\Users\PRIVATE_OWNER\driver")]
    [InlineData("NVIDIA GPU PRIVATE_SERIALNUMBER_521")]
    [InlineData("NVIDIA GPU PRIVATE_ORDER_521")]
    [InlineData("NVIDIA GPU\u202EPRIVATE_PERSON")]
    public void PrivateOrInstanceDeviceNameUsesSafePnPFallback(
        string name)
    {
        using var zip = Zip(("gpu/display.inf", Nvidia, "32.0.0.1"));
        var row = Assert.Single(
            Compare(zip, Device(Nvidia, name)).Findings);

        Assert.Equal("Scheda video (nome non disponibile)", row.DeviceName);
        Assert.Equal("display.inf", row.InfName);
        Assert.True(row.HasExactHardwareId);
        Assert.DoesNotContain("PRIVATE_", row.Summary);
        Assert.DoesNotContain(@"C:\Users", row.Summary);
        Assert.DoesNotContain(@"PCI\", row.Summary);
        Assert.False(row.Summary.Contains('\u202E'));
    }

    [Theory]
    [InlineData(@"C:\Users\PRIVATE_OWNER\driver.dll")]
    [InlineData("31.0.0.1\nPRIVATE_SERIAL_521")]
    [InlineData("PRIVATE_DRIVER_VERSION_521")]
    [InlineData("31.0.0.1\u202E")]
    public void InvalidInstalledDriverVersionCannotLeakViaFinding(
        string privateVersion)
    {
        using var zip = Zip(("gpu/display.inf", Nvidia, "32.0.0.1"));
        var row = Assert.Single(
            Compare(zip, Device(Nvidia, "NVIDIA Display",
                privateVersion)).Findings);

        Assert.Equal("non verificata", row.InstalledVersion);
        Assert.Equal(LocalOemDriverComparison.VersionRelation.Unavailable,
            row.VersionComparison);
        Assert.False(row.IsPotentialNewer);
        Assert.Contains("NVIDIA Display", row.Summary);
        Assert.DoesNotContain("PRIVATE_", row.Summary);
        Assert.DoesNotContain(@"C:\Users", row.Summary);
        Assert.False(row.Summary.Contains('\u202E'));
    }

    [Fact]
    public void NormalNvidiaKillerRealtekLabelsAndVersionEvidenceRemainReadable()
    {
        using var zip = Zip(
            ("gpu/display.inf", Nvidia, "32.0.0.1"),
            ("network/killer_wifi.inf", Killer, "24.1.0.1"),
            ("audio/realtek_hd.inf", Realtek, "10.1.0.1"));
        var report = Compare(zip,
            Device(Nvidia, "NVIDIA GeForce RTX 4060 Laptop GPU"),
            Device(Killer, "Intel(R) Killer Wi-Fi 6E AX1675i",
                "23.1.0.1", "Net"),
            Device(Realtek, "Realtek High Definition Audio",
                "9.1.0.1", "Media"));

        Assert.Equal(3, report.TotalMatchedDevices);
        Assert.Equal(3, report.PnpMatchedDeviceOrdinals!.Count);
        Assert.Equal(3, report.PotentialNewerDevices);
        Assert.Contains(report.Findings, row =>
            row.DeviceName == "NVIDIA GeForce RTX 4060 Laptop GPU" &&
            row.InfName == "display.inf");
        Assert.Contains(report.Findings, row =>
            row.DeviceName == "Intel(R) Killer Wi-Fi 6E AX1675i" &&
            row.InfName == "killer_wifi.inf");
        Assert.Contains(report.Findings, row =>
            row.DeviceName == "Realtek High Definition Audio" &&
            row.InfName == "realtek_hd.inf");
        Assert.DoesNotContain("PRIVATE_INSTANCE", report.Summary);
        Assert.All(report.Findings, row =>
        {
            Assert.Equal(LocalOemDriverComparison.VersionRelation.NewerHint,
                row.VersionComparison);
            Assert.DoesNotContain("PRIVATE_INSTANCE", row.Summary);
        });
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedCompatible);
    }

    [Fact]
    public void SecretHighVersionInfDoesNotLeakAndRankingStillUsesVersion()
    {
        using var zip = Zip(
            ("gpu/public.inf", Nvidia, "31.0.0.2"),
            ("gpu/PRIVATE_ORDER_521_NEW.inf", Nvidia, "32.0.0.1"));
        var report = Compare(zip, Device(Nvidia, "NVIDIA GPU"));
        var chosen = Assert.Single(report.Findings);

        Assert.Equal("file INF OEM (nome riservato)", chosen.InfName);
        Assert.Equal("32.0.0.1", chosen.PackageVersion);
        Assert.Equal(1, report.TotalMatchedDevices);
        Assert.Equal(1, report.AdditionalMatchesOmitted);
        Assert.True(chosen.IsPotentialNewer);
        Assert.DoesNotContain("PRIVATE_ORDER", chosen.Summary);
        Assert.False(report.IsInstallable);
    }
}
