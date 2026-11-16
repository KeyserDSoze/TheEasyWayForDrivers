using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #511: explicit Firmware/BIOS in a saved PCSpecialist package basename
/// cannot be disguised as an ordinary GPU, Wi-Fi, Realtek or app category.
/// All names are review-only, never authenticated OEM package evidence.
/// </summary>
public sealed class PcSpecialistFirmwareNamePriorityTests
{
    private const string Host =
        "https://www.pcspecialist.co.uk/download-drivers/driver.zip";

    private static string Page(IEnumerable<string> names) =>
        "<html><title>Download Drivers / Manuals</title>" +
        "<h2>Download Drivers / Manuals</h2>" +
        "<table><tr><th>Date Uploaded</th><th>File Size</th></tr>" +
        string.Concat(names.Select(name =>
            "<tr><td><a href=\"" + Host + "\">" + name +
            "</a></td></tr>")) +
        "</table></html>";

    [Theory]
    [InlineData("W11 NVIDIA GPU Firmware.exe")]
    [InlineData("W11 Nvidia VGA BIOS.zip")]
    [InlineData("W11 Intel Graphics Firmware.zip")]
    [InlineData("W11 Killer WiFi BIOS.zip")]
    [InlineData("W11 Intel Bluetooth Firmware.zip")]
    [InlineData("W11 Realtek Audio Firmware.cab")]
    [InlineData("W11 Realtek Ethernet BIOS.zip")]
    [InlineData("W11 Control Center Firmware.exe")]
    [InlineData("W11 BIOS Bluetooth installer.msi")]
    public void ExplicitFirmwareOrBiosBeatsDriverAndApplicationNames(
        string basename)
    {
        var row = Assert.Single(
            PcSpecialistPortalSnapshot.Parse(Page([basename])).Packages);
        Assert.Equal("Piattaforma / firmware", row.Category);
        Assert.Equal("Verifica manuale necessaria", row.Type);
        Assert.False(row.IsInstallable);
        Assert.False(row.IsHardwareMatched);
        Assert.False(row.IsVerifiedOfficial);
    }

    [Theory]
    [InlineData("W11 NVIDIA GPU Driver.zip", "NVIDIA · grafica")]
    [InlineData("W11 Intel Graphics.zip", "Intel · grafica")]
    [InlineData("W11 Intel Killer WiFi.zip", "Wi-Fi / Killer")]
    [InlineData("W11 Intel Bluetooth.zip", "Bluetooth")]
    [InlineData("W11 Realtek Audio.zip", "Realtek · audio")]
    [InlineData("W11 Realtek LAN.zip", "Realtek · rete")]
    [InlineData("W11 Control Center.exe", "Software OEM")]
    [InlineData("W11 Intel ME.zip", "Piattaforma / firmware")]
    public void OriginalOrdinaryAndPlatformCategoriesAreRetained(
        string basename, string category)
    {
        var row = Assert.Single(
            PcSpecialistPortalSnapshot.Parse(Page([basename])).Packages);
        Assert.Equal(category, row.Category);
        Assert.False(row.IsInstallable);
    }

    [Fact]
    public void FirmwareKeepsItsOwnBalancedCategoryInLargeHtmlPreview()
    {
        var chipset = Enumerable.Range(0, 160)
            .Select(i => $"W11 Intel Chipset {i:D3}.zip");
        var names = chipset.Concat([
            "W11 NVIDIA GPU Firmware.exe",
            "W11 Intel Killer WiFi BIOS.zip",
            "W11 Realtek Audio Firmware.zip",
            "W11 NVIDIA GPU Driver.zip",
            "W11 Intel Killer WiFi.zip",
            "W11 Realtek Audio.zip"
        ]).ToArray();
        var report = PcSpecialistPortalSnapshot.Parse(Page(names));

        Assert.True(report.IsPartial);
        Assert.Equal(166, report.TotalDistinct);
        Assert.Equal(PcSpecialistPortalSnapshot.MaximumDisplayedPackages,
            report.Packages.Count);
        Assert.Equal(166, report.AllParsedPackages.Count);
        Assert.Equal(3, report.Packages.Count(row =>
            row.Category == "Piattaforma / firmware"));
        Assert.Contains(report.Packages, row =>
            row.Name == "W11 NVIDIA GPU Firmware.exe" &&
            row.Category == "Piattaforma / firmware");
        Assert.Contains(report.Packages, row =>
            row.Name == "W11 Realtek Audio Firmware.zip" &&
            row.Type == "Verifica manuale necessaria");
        Assert.Contains(report.Packages, row =>
            row.Name == "W11 NVIDIA GPU Driver.zip" &&
            row.Category == "NVIDIA · grafica");
        Assert.Contains(report.Packages, row =>
            row.Name == "W11 Intel Killer WiFi.zip" &&
            row.Category == "Wi-Fi / Killer");
        Assert.Contains(report.Packages, row =>
            row.Name == "W11 Realtek Audio.zip" &&
            row.Category == "Realtek · audio");
        Assert.All(report.Packages, row =>
            Assert.False(row.IsInstallable));
        Assert.DoesNotContain("driver.zip", report.Summary);
    }

    [Fact]
    public void FirmwareNamesStayFirmwareInLocallyObservedPreview()
    {
        var names = new[]
        {
            "W11 Nvidia GPU Firmware.exe",
            "W11 Nvidia GPU Driver.zip",
            "W11 Intel Killer BIOS.zip"
        };
        var original = PcSpecialistPortalSnapshot.Parse(Page(names));
        var displayed = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
            original.AllParsedPackages,
            ["W11 Nvidia GPU Firmware.exe", "W11 Intel Killer BIOS.zip"]);
        Assert.Equal(2, displayed.Count(row =>
            row.Category == "Piattaforma / firmware"));
        Assert.Contains(displayed, row =>
            row.Name == "W11 Nvidia GPU Driver.zip" &&
            row.Category == "NVIDIA · grafica");
    }
}
