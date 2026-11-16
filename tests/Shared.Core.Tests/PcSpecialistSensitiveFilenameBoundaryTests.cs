using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #539: a PCSpecialist portal filename consisting entirely of valid
/// ASCII can still contain account/order/credential metadata. No unsafe
/// basename should cross into UI previews or local OEM reconciliation.
/// </summary>
public sealed class PcSpecialistSensitiveFilenameBoundaryTests
{
    private const string Storage =
        "https://drivers.d498d9367a7607b3ba8d3ee7c18071d8.r2.cloudflarestorage.com";

    private static string Link(string name) =>
        "<a href=\"" + Storage + "/90/19816/" +
        Uri.EscapeDataString(name) + "\">" + name + "</a>";

    private static string Page(params string[] links) =>
        "<html><h2>Download Drivers / Manuals</h2>" +
        "<div>Date Uploaded File Size</div>" +
        string.Concat(links) + "</html>";

    [Theory]
    [InlineData("W11 NVIDIA PRIVATE_MACHINE.zip")]
    [InlineData("W11 Killer CUSTOMER_12456.cab")]
    [InlineData("W11 Realtek SECRET_KEY.exe")]
    [InlineData("W11 NVIDIA account_3377.zip")]
    [InlineData("W11 Driver UserName_PC.zip")]
    [InlineData("W11 GPU HOSTNAME_WORKSTATION.zip")]
    [InlineData("W11 GPU PassWord_123.zip")]
    [InlineData("W11 GPU EMAIL_PERSON.zip")]
    [InlineData("W11 Intel DEVICEID_773.zip")]
    [InlineData("W11 Intel INSTANCEID_991.zip")]
    [InlineData("W11 GPU UUID_123456.zip")]
    [InlineData("W11 GPU ORDER_9988.zip")]
    [InlineData("W11 GPU SERIAL_9988.zip")]
    [InlineData("W11 GPU Invoice_9988.zip")]
    [InlineData("W11 GPU TOKEN_9988.zip")]
    public void RejectsSensitiveMarkersWithoutHidingPublicDriverPackages(
        string sensitive)
    {
        Assert.False(PcSpecialistPortalSnapshot.IsSafePackageName(sensitive));

        var parsed = PcSpecialistPortalSnapshot.Parse(Page(
            Link(sensitive),
            Link("W11 Nvidia VGA.zip"),
            Link("W11 Intel Killer WiFi.zip"),
            Link("W11 Realtek Audio.cab")));

        Assert.Equal(3, parsed.TotalDistinct);
        Assert.Equal(0, parsed.Omitted);
        Assert.Equal(3, parsed.AllParsedPackages.Count);
        Assert.DoesNotContain(parsed.AllParsedPackages,
            p => string.Equals(p.Name, sensitive,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, parsed.Packages.Count);
        Assert.Contains(parsed.Packages, p =>
            p.Name == "W11 Nvidia VGA.zip" &&
            p.Category == "NVIDIA · grafica");
        Assert.Contains(parsed.Packages, p =>
            p.Name == "W11 Intel Killer WiFi.zip" &&
            p.Category == "Wi-Fi / Killer");
        Assert.Contains(parsed.Packages, p =>
            p.Name == "W11 Realtek Audio.cab" &&
            p.Category == "Realtek · audio");
        Assert.DoesNotContain(sensitive, parsed.Summary);
        Assert.DoesNotContain(sensitive,
            string.Join("; ", parsed.AllParsedPackages.Select(p => p.Name)));
    }

    [Fact]
    public void OnlySensitiveFilenamesFailClosedWithoutEchoingDetails()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.Parse(Page(
                Link("W11 NVIDIA PRIVATE_PC.zip"),
                Link("W11 Killer CUSTOMER_7733.zip"))));

        Assert.Contains("Nessun nome di pacchetto", error.Message);
        Assert.DoesNotContain("PRIVATE_PC", error.Message);
        Assert.DoesNotContain("CUSTOMER_7733", error.Message);
    }

    [Fact]
    public void DirectLegacyInputsCannotBypassSameGateInPreviewPlannerOrReconciliation()
    {
        var privateName = "W11 Nvidia SECRET_MACHINE.zip";
        var publicName = "W11 Nvidia VGA.zip";
        var privateRow = new PcSpecialistPortalSnapshot.Package(
            privateName, "NVIDIA · grafica", "Pacchetto da verificare");
        var publicRow = new PcSpecialistPortalSnapshot.Package(
            publicName, "NVIDIA · grafica", "Pacchetto da verificare");

        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
                [privateRow, publicRow], [publicName]));
        // Preferred basenames are checked BEFORE the folder exists or
        // any private local OEM archive is accessed.
        var plannerError = Assert.Throws<InvalidDataException>(() =>
            PcSpecialistLocalCatalogPlan.Create(
                Path.Combine(Path.GetTempPath(), "PRIVATE_ORDER_MISSING"),
                preferredPackageNames: [privateName]));
        Assert.DoesNotContain(privateName, plannerError.Message);
        Assert.DoesNotContain("PRIVATE_ORDER_MISSING", plannerError.Message);

        var reconciliationError = Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.Compare(
                [privateRow, publicRow], []));
        Assert.DoesNotContain(privateName, reconciliationError.Message);
    }

    [Fact]
    public void PublicCatalogNamesStillAllowObservedLocalPrioritization()
    {
        var publicNames = new[]
        {
            "W11 Nvidia VGA.zip", "W11 Intel Killer WiFi.zip",
            "W11 Realtek Audio.cab", "W11 Intel Chipset 015.zip"
        };
        Assert.All(publicNames, name =>
            Assert.True(PcSpecialistPortalSnapshot.IsSafePackageName(name)));

        var report = PcSpecialistPortalSnapshot.Parse(Page(
            publicNames.Select(Link).ToArray()));
        var selected = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
            report.AllParsedPackages, ["W11 Realtek Audio.cab"]);

        Assert.Equal(4, selected.Count);
        Assert.Contains(selected, p => p.Name == "W11 Realtek Audio.cab");
        Assert.Equal(report.TotalDistinct, report.AllParsedPackages.Count);
        Assert.All(selected, item => Assert.False(item.IsInstallable));
    }
}
