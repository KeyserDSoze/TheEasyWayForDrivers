using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class PcSpecialistPortalSnapshotTests
{
    private const string Storage =
        "https://drivers.d498d9367a7607b3ba8d3ee7c18071d8.r2.cloudflarestorage.com";
    private const string Signed =
        "?X-Amz-Algorithm=AWS4-HMAC-SHA256&amp;X-Amz-Credential=PRIVATE_ORDER_TOKEN&amp;X-Amz-Signature=PRIVATE_TOKEN";
    private const string Header =
        "<html><title>Download Drivers / Manuals</title>" +
        "<h2>Download Drivers / Manuals</h2>" +
        "<p>Order No. 123456789 PRIVATE_ACCOUNT</p>" +
        "<table><tr><th>Category</th><th>Product</th>" +
        "<th>File</th><th>Date Uploaded</th><th>File Size</th></tr>";

    private static string Link(string name, string? url = null) =>
        "<tr><td>Chassis</td><td>16&quot; Recoil&reg;</td><td>" +
        "<a href=\"" + (url ?? Storage + "/90/19816/" +
            Uri.EscapeDataString(name) + Signed) + "\">" +
        name + "</a></td><td>06-02-2024</td><td>12.1 Mb</td></tr>";

    private static string Page(params string[] rows) =>
        Header + string.Concat(rows) + "</table></html>";

    [Fact]
    public void RecoilOfficialDownloadListShowsMultipleOemPackagesWithoutAnyDownload()
    {
        var html = Page(
            Link("W11 Nvidia VGA.zip"),
            Link("W11 Intel WiFi.zip"),
            Link("W11 Intel Bluetooth.zip"),
            Link("W11 Intel Chipset.zip"),
            Link("W11 Intel ME.zip"),
            Link("W11 Realtek Audio.zip"),
            Link("W11 Realtek LAN.zip"),
            Link("W11 Apps.zip"),
            Link("W11 Nahimic Audio (Certain models only).zip"),
            Link("W11 00-Installation Guide.pdf"),
            Link("W11 Nvidia VGA.zip"));
        var report = PcSpecialistPortalSnapshot.Parse(html);
        Assert.Equal(9, report.TotalDistinct);
        Assert.Equal(9, report.Packages.Count);
        Assert.Equal(0, report.Omitted);
        Assert.False(report.IsPartial);
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Nvidia VGA.zip" &&
                p.Category == "NVIDIA · grafica");
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Intel WiFi.zip" &&
                p.Category == "Wi-Fi / Killer");
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Intel Bluetooth.zip" &&
                p.Category == "Bluetooth");
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Realtek Audio.zip" &&
                p.Category == "Realtek · audio");
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Realtek LAN.zip" &&
                p.Category == "Realtek · rete");
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Intel ME.zip" &&
                p.Category == "Piattaforma / firmware");
        Assert.Contains(report.Packages,
            p => p.Name == "W11 Apps.zip" &&
                p.Category == "Software OEM");
        Assert.All(report.Packages, p =>
        {
            Assert.False(p.IsInstallable);
            Assert.False(p.IsHardwareMatched);
            Assert.False(p.IsVerifiedOfficial);
            Assert.Contains("NON verificate", p.Detail);
            Assert.DoesNotContain("PRIVATE", p.Name);
            Assert.DoesNotContain("PRIVATE", p.Detail);
            Assert.DoesNotContain("X-Amz", p.Name);
        });
        Assert.False(report.IsInstallable);
        Assert.False(report.IsHardwareMatched);
        Assert.False(report.IsOfficialSourceVerified);
        Assert.Contains("NON sono aggiornamenti WUA", report.Summary);
        Assert.DoesNotContain("123456789", report.Summary);
        Assert.DoesNotContain("PRIVATE", report.Summary);
        Assert.DoesNotContain("cloudflarestorage.com", report.Summary);
    }

    [Fact]
    public void OnlyAllowedHttpsPcsAndOfficialCloudflareStorageLinksAreRead()
    {
        Assert.True(PcSpecialistPortalSnapshot.IsAllowedDownloadUrl(
            Storage + "/90/19816/W11%20Nvidia%20VGA.zip" + Signed));
        Assert.True(PcSpecialistPortalSnapshot.IsAllowedDownloadUrl(
            "https://www.pcspecialist.co.uk/drivers/recoil/W11.zip"));
        Assert.True(PcSpecialistPortalSnapshot.IsAllowedDownloadUrl(
            "https://pcspecialist.co.uk/download-drivers/driver.zip"));
        foreach (var url in new[]
        {
            "http://www.pcspecialist.co.uk/driver.zip",
            "https://evil.example/W11.zip",
            "https://www.pcspecialist.co.uk.evil.example/driver.zip",
            "https://evil@www.pcspecialist.co.uk/driver.zip",
            "https://www.pcspecialist.co.uk:8443/driver.zip",
            "https://drivers.badhost.r2.cloudflarestorage.com/driver.zip",
            "https://drivers.1234.r2.cloudflarestorage.com/driver.zip",
            "file:///C:/Private/driver.zip",
            "javascript:alert(1)",
            "https://www.pcspecialist.ie.evil.example/driver.zip"
        })
            Assert.False(PcSpecialistPortalSnapshot.IsAllowedDownloadUrl(url));

        var report = PcSpecialistPortalSnapshot.Parse(Page(
            Link("W11 Nvidia VGA.zip",
                "https://evil.example/nvidia-driver.zip"),
            Link("W11 Realtek Audio.zip"),
            Link("W11 Bluetooth.zip",
                "https://www.pcspecialist.co.uk.evil.example/driver.zip")));
        Assert.Equal("W11 Realtek Audio.zip", Assert.Single(report.Packages).Name);
    }

    [Fact]
    public void HtmlTagsAndEntitiesAreDecodedOnlyInSafeFileNames()
    {
        var html = Page(
            "<tr><td><a href=\"" + Storage +
            "/file.zip?X-Amz-Signature=TOKEN\"><span>W11 Intel " +
            "<b>Bluetooth</b>.zip</span></a></td></tr>",
            "<tr><td><a href=\"" + Storage +
            "/file2.zip\">W11 NVIDIA &amp; VGA.zip</a></td></tr>",
            Link("W11 Nvidia VGA.zip<script>alert(1)</script>"),
            Link("W11 ME.exe"),
            Link("W11 Tool.msi"),
            Link("W11 Firmware.7z"));
        var found = PcSpecialistPortalSnapshot.Parse(html);
        Assert.Contains(found.Packages, p =>
            p.Name == "W11 Intel Bluetooth.zip");
        Assert.Contains(found.Packages, p =>
            p.Name == "W11 NVIDIA & VGA.zip");
        Assert.Contains(found.Packages, p =>
            p.Name == "W11 ME.exe");
        Assert.Contains(found.Packages, p =>
            p.Name == "W11 Tool.msi");
        Assert.Contains(found.Packages, p =>
            p.Name == "W11 Firmware.7z");
        Assert.DoesNotContain(found.Packages,
            p => p.Name.Contains("<", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("W11 Driver PRIVATE_ORDER_1234.zip")]
    [InlineData("W11 Driver\\User.zip")]
    [InlineData("W11 Driver/Users.zip")]
    [InlineData("W11 driver@secret.zip")]
    [InlineData("W11 token=SECRET.zip")]
    [InlineData("W11 serial_12345.zip")]
    [InlineData("W11 invoice_12345.zip")]
    [InlineData("W11 .. driver.zip")]
    [InlineData("W11 GPU\u202e.zip")]
    [InlineData("W11 GPU\n.zip")]
    public void SensitiveOrPathLikeFileNamesAreNeverExposed(string name)
    {
        var report = PcSpecialistPortalSnapshot.Parse(Page(
            Link(name), Link("W11 Realtek Audio.zip")));
        Assert.Equal(1, report.TotalDistinct);
        Assert.Equal("W11 Realtek Audio.zip", Assert.Single(report.Packages).Name);
    }

    [Theory]
    [InlineData("<html>Not a driver page</html>")]
    [InlineData("<html>Download Drivers / Manuals</html>")]
    [InlineData("<html>Date Uploaded File Size</html>")]
    public void UnrecognizedOrEmptyPortalsFailClosed(string html)
    {
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.Parse(html));
    }

    [Fact]
    public void OversizedAndTooManyAnchorsFailWithoutIncompleteSuccess()
    {
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.Parse(
                Page(Link("W11 Intel WiFi.zip")) +
                new string('x', PcSpecialistPortalSnapshot.MaximumHtmlCharacters)));
        var anchors = Enumerable.Range(0, PcSpecialistPortalSnapshot.MaximumLinks + 1)
            .Select(i => Link($"W11 Intel Chipset {i}.zip")).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.Parse(Page(anchors)));
    }

    [Fact]
    public void LargeCatalogIsExplicitlyPartialAndDoesNotInventDriverMatches()
    {
        var links = Enumerable.Range(0, 110)
            .Select(i => Link($"W11 Intel Chipset {i:D3}.zip")).ToArray();
        var result = PcSpecialistPortalSnapshot.Parse(Page(links));
        Assert.True(result.IsPartial);
        Assert.Equal(110, result.TotalDistinct);
        Assert.Equal(PcSpecialistPortalSnapshot.MaximumDisplayedPackages,
            result.Packages.Count);
        Assert.Equal(110 - PcSpecialistPortalSnapshot.MaximumDisplayedPackages,
            result.Omitted);
        Assert.All(result.Packages, p => Assert.False(p.IsInstallable));
        Assert.Contains("PARZIALE", result.Summary);
    }

    [Fact]
    public void CancelledImportNeverReturnsStaleResults()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            PcSpecialistPortalSnapshot.Parse(
                Page(Link("W11 NVIDIA VGA.zip")), cts.Token));
    }

    [Fact]
    public void MissingValidPackageFilesCannotBeMistakenForEmptyDriverCatalog()
    {
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.Parse(Page(
                Link("W11 Guide.pdf"),
                Link("W11 Nvidia.zip", "http://www.pcspecialist.co.uk/not-safe"))));
    }

    [Fact]
    public void LateNvidiaKillerRealtekPackagesSurviveNinetySixRowBudget()
    {
        var bulk = Enumerable.Range(0, 160)
            .Select(i => Link($"W11 Intel Chipset {i:D3}.zip")).ToArray();
        var important = new[]
        {
            "W11 Nvidia VGA.zip",
            "W11 Killer WiFi.zip",
            "W11 Realtek Audio.zip",
            "W11 Realtek LAN.zip",
            "W11 Intel Bluetooth.zip",
            "W11 Control Centre.msi"
        };
        var links = bulk.Concat(important.Select(name => Link(name))).ToArray();
        var first = PcSpecialistPortalSnapshot.Parse(Page(links));
        var reversed = PcSpecialistPortalSnapshot.Parse(Page(
            links.Reverse().ToArray()));

        Assert.Equal(166, first.TotalDistinct);
        Assert.Equal(96, first.Packages.Count);
        Assert.Equal(70, first.Omitted);
        Assert.True(first.IsPartial);
        Assert.Equal(first.TotalDistinct, reversed.TotalDistinct);
        Assert.Equal(first.Packages.Select(p => p.Name),
            reversed.Packages.Select(p => p.Name));
        foreach (var name in important)
            Assert.Contains(first.Packages, p => p.Name == name);

        Assert.All(first.Packages, p =>
        {
            Assert.False(p.IsInstallable);
            Assert.False(p.IsHardwareMatched);
            Assert.False(p.IsVerifiedOfficial);
        });
        Assert.DoesNotContain("PRIVATE_ORDER", first.Summary);
        Assert.DoesNotContain("X-Amz", first.Summary);
        Assert.DoesNotContain("cloudflarestorage.com", first.Summary);
        Assert.Contains("PARZIALE", first.Summary);
    }

    [Fact]
    public void LargeMixedPortalBalancesCategoriesAndStillDeduplicates()
    {
        var chipset = Enumerable.Range(0, 125)
            .Select(i => Link($"W11 Intel Chipset {i:D3}.zip"));
        var audio = Enumerable.Range(0, 125)
            .Select(i => Link($"W11 Realtek Audio {i:D3}.zip"));
        var html = Page(chipset.Concat(audio)
            .Append(Link("W11 Nvidia VGA.zip"))
            .Append(Link("W11 Nvidia VGA.zip")).ToArray());

        var result = PcSpecialistPortalSnapshot.Parse(html);
        Assert.Equal(251, result.TotalDistinct);
        Assert.Equal(96, result.Packages.Count);
        Assert.Equal(155, result.Omitted);
        Assert.Contains(result.Packages, p => p.Name == "W11 Nvidia VGA.zip");
        Assert.InRange(result.Packages.Count(p =>
            p.Category == "Realtek · audio"), 45, 48);
        Assert.InRange(result.Packages.Count(p =>
            p.Category == "Chipset / periferiche"), 45, 48);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsHardwareMatched);
    }

}
