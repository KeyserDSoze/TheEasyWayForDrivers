using System.IO.Compression;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Real selected local ZIPs outside the first 96 PCSpecialist HTML rows
/// must be visible by exact basename after bounded read-only comparison.
/// Never treat a saved HTML filename as a signed or installable driver.
/// </summary>
public sealed class PcSpecialistLocallyObservedPreviewTests
{
    private const string Killer = @"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56";
    private const string Gpu = @"PCI\VEN_10DE&DEV_28A0&SUBSYS_16A217AA";
    private const string Host =
        "https://www.pcspecialist.co.uk/download-drivers/saved.zip";

    private static string Page(IEnumerable<string> names) =>
        "<html><h1>Download Drivers / Manuals</h1>" +
        "<table><tr><th>Date Uploaded</th><th>File Size</th></tr>" +
        string.Concat(names.Select(n =>
            "<tr><td><a href=\"" + Host + "\">" + n + "</a></td></tr>")) +
        "</table></html>";

    private static DriverInfo Device(string id) =>
        new("PRIVATE_ORDER_DEVICE", "Generic Killer adapter", "Microsoft",
            "Net", "Microsoft", "23.0.0.0", null, "PRIVATE_oem42.inf",
            true, true, 0, [id], []);

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\nClass=Net\n" +
        "DriverVer=10/04/2026,32.0.15.7000\n" +
        "[Manufacturer]\n%Maker%=Models,NTamd64.10.0...22000\n" +
        "[Models.NTamd64.10.0...22000]\n%Model%=Install," + id + "\n";

    private static void WriteZip(string folder, string name, string model)
    {
        using var zip = ZipFile.Open(Path.Combine(folder, name),
            ZipArchiveMode.Create);
        using var writer = new StreamWriter(
            zip.CreateEntry("drivers/killer.inf").Open(),
            new UTF8Encoding(false));
        writer.Write(Inf(model));
    }

    [Fact]
    public void LocalKillerZipAfterNinetySixSameCategoryNamesBecomesVisible()
    {
        const string late = "W11 Intel Chipset 169.zip";
        var htmlNames = Enumerable.Range(0, 170)
            .Select(i => $"W11 Intel Chipset {i:D3}.zip")
            .Append("W11 Nvidia VGA.zip").ToArray();
        var html = Page(htmlNames);
        var saved = PcSpecialistPortalSnapshot.Parse(html);
        Assert.Equal(171, saved.TotalDistinct);
        Assert.Equal(96, saved.Packages.Count);
        Assert.Equal(171, saved.AllParsedPackages.Count);
        Assert.Equal(75, saved.Omitted);
        Assert.DoesNotContain(saved.Packages, p => p.Name == late);

        var folder = Directory.CreateTempSubdirectory(
            "OmegaDrive-LocalPortalOver96-");
        try
        {
            WriteZip(folder.FullName, late, Killer);
            var plan = PcSpecialistLocalCatalogPlan.Create(folder.FullName,
                preferredPackageNames: saved.Packages.Select(p => p.Name).ToArray());
            var sourcePath = Assert.Single(plan.Archives);
            Assert.Equal(late, Path.GetFileName(sourcePath));

            using var stream = File.OpenRead(sourcePath);
            var inspected = LocalOemDriverComparison.Compare(
                stream, [Device(Killer)], "x64", 10, 0, 26100);
            Assert.Single(inspected.Findings);
            Assert.Equal(1, inspected.TotalMatchedDevices);
            Assert.False(inspected.IsInstallable);

            var source = PcSpecialistPackageReconciliation.LocalSource
                .FromArchive(late, inspected);
            var before = PcSpecialistPackageReconciliation.Compare(
                saved.Packages, [source]);
            Assert.Equal(0, before.WithPnPEvidence); // preview hid it

            var afterPreview = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
                saved.AllParsedPackages, [source.FileName]);
            Assert.Equal(96, afterPreview.Count);
            Assert.Contains(afterPreview, p => p.Name == late);
            Assert.Contains(afterPreview, p => p.Name == "W11 Nvidia VGA.zip");
            Assert.DoesNotContain(afterPreview, p =>
                p.Name.Contains("PRIVATE", StringComparison.Ordinal));

            var after = PcSpecialistPackageReconciliation.Compare(
                afterPreview, [source]);
            Assert.Equal(1, after.WithPnPEvidence);
            var row = Assert.Single(after.Rows.Where(r =>
                r.Package.Name == late));
            Assert.Equal(
                PcSpecialistPackageReconciliation.State.DeclaredPnpMatch,
                row.Status);
            Assert.Contains("1 dispositivi", row.Detail);
            Assert.False(row.IsInstallable);
            Assert.False(row.IsOfficiallyVerified);
            Assert.False(row.IsWindowsApplicable);
            Assert.DoesNotContain("PRIVATE", after.Summary);
            Assert.DoesNotContain(Killer, after.Summary);
            Assert.DoesNotContain(late, after.Summary);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ObservedNamesWinOnlyExactMatchesAndAreOrderIndependent()
    {
        const string late = "W11 Intel Chipset 139.zip";
        var names = Enumerable.Range(0, 160)
            .Select(i => $"W11 Intel Chipset {i:D3}.zip")
            .Append("W11 Nvidia VGA.zip")
            .Append("W11 Realtek Audio.zip").ToArray();
        var first = PcSpecialistPortalSnapshot.Parse(Page(names));
        var reversed = PcSpecialistPortalSnapshot.Parse(
            Page(names.Reverse().ToArray()));

        var observed = new[] {
            late.ToUpperInvariant(), "W11 Intel Chipset 150.zip",
            @"C:\PRIVATE_ORDER\W11 Intel Chipset 158.zip",
            "W11 Intel Chipset 001 new.zip",
            "W11 token_PRIVATE.zip"
        };
        var a = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
            first.AllParsedPackages, observed);
        var b = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
            reversed.AllParsedPackages, observed.Reverse().ToArray());
        Assert.Equal(a.Select(x => x.Name), b.Select(x => x.Name));
        Assert.Equal(96, a.Count);
        Assert.Contains(a, p => p.Name == late);
        Assert.Contains(a, p => p.Name == "W11 Intel Chipset 150.zip");
        Assert.DoesNotContain(a, p => p.Name == "W11 Intel Chipset 158.zip");
        Assert.Contains(a, p => p.Name == "W11 Nvidia VGA.zip");
        Assert.Contains(a, p => p.Name == "W11 Realtek Audio.zip");
        Assert.All(a, p => Assert.False(p.IsInstallable));
        Assert.DoesNotContain("PRIVATE", string.Join(" ", a.Select(p => p.Name)));
    }

    [Fact]
    public void OneHundredAndTenObservedChipsetPackagesRemainCategoryBalanced()
    {
        var names = Enumerable.Range(0, 170)
            .Select(i => $"W11 Intel Chipset {i:D3}.zip")
            .Concat(Enumerable.Range(0, 10)
                .Select(i => $"W11 Realtek Audio {i:D3}.zip"))
            .Append("W11 Nvidia VGA.zip")
            .ToArray();
        var all = PcSpecialistPortalSnapshot.Parse(Page(names)).AllParsedPackages;
        var observed = names.Where(n => n.StartsWith(
            "W11 Intel Chipset", StringComparison.Ordinal)).ToArray();
        var chosen = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
            all, observed);
        Assert.Equal(96, chosen.Count);
        Assert.All(chosen, p => Assert.Contains(p.Name, observed));
        // With >96 observed entries of ONE category, fairness applies
        // within the observed set; no unobserved file is called present.
        Assert.DoesNotContain(chosen, p => p.Name == "W11 Nvidia VGA.zip");

        var none = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(all, []);
        Assert.Contains(none, p => p.Name == "W11 Nvidia VGA.zip");
        Assert.Equal(96, none.Count);
    }

    [Fact]
    public void AmbiguousPnpInstancesFromArchiveMakeHtmlReviewPartial()
    {
        var report = new LocalOemDriverComparison.Report(
            InfExamined: 1, DevicesExamined: 2, Findings: [],
            PotentialNewerDevices: 0, Notes: [], TotalMatchedDevices: 0)
        {
            MatchedDeviceOrdinals = [],
            PnpMatchedDeviceOrdinals = [],
            ExcludedAmbiguousPnpInstances = 1
        };
        var local = PcSpecialistPackageReconciliation.LocalSource
            .FromArchive("W11 Nvidia VGA.zip", report);
        Assert.True(local.Partial);
        Assert.Equal(0, local.PnpMatches);
        var row = Assert.Single(
            PcSpecialistPackageReconciliation.Compare(
                [new PcSpecialistPortalSnapshot.Package(
                    "W11 Nvidia VGA.zip", "NVIDIA · grafica", "verifica")],
                [local]).Rows);
        Assert.Equal(
            PcSpecialistPackageReconciliation.State.ExaminedWithoutVisibleMatch,
            row.Status);
        Assert.Contains("Analisi parziale", row.Detail);
        Assert.Contains("identità PnP ambigue", row.Detail);
        Assert.Contains("non prova assenza", row.Detail);
        Assert.False(row.IsInstallable);
    }

    [Fact]
    public void UntrustedHtmlNamesAndMoreThan2048InputsAreNotPromoted()
    {
        var all = PcSpecialistPortalSnapshot.Parse(Page(
            ["W11 Nvidia VGA.zip", "W11 Killer WiFi.zip"])).AllParsedPackages;
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
                all.Concat(all).ToArray(), ["W11 Nvidia VGA.zip"]));
        var oversized = Enumerable.Range(0,
            PcSpecialistPortalSnapshot.MaximumLinks + 1)
            .Select(i => new PcSpecialistPortalSnapshot.Package(
                $"W11 Chipset {i}.zip", "Chipset", "read-only")).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPortalSnapshot.ChoosePreviewForLocal(oversized, []));

        var selected = PcSpecialistPortalSnapshot.ChoosePreviewForLocal(
            all, [@"C:\PRIVATE_ORDER\W11 Nvidia VGA.zip",
                "W11 Nvidia VGA new.zip"]);
        Assert.Equal(2, selected.Count);
        Assert.All(selected, p => Assert.False(p.IsInstallable));
    }
}
