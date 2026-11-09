using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Exact-name reconciliation of a user-saved HTML list against OEM archive
/// analyses. Presence is not authenticity; a PnP INF declaration is not
/// an applicable, signed or installable driver.
/// </summary>
public sealed class PcSpecialistPackageReconciliationTests
{
    private static PcSpecialistPortalSnapshot.Package Package(string name) =>
        new(name, "Pacchetto OEM", "Solo consultazione");

    private static PcSpecialistPackageReconciliation.LocalSource Checked(
        string name, int real = 0, int weak = 0, bool partial = false) =>
        new(name, true, real, weak, partial);

    [Fact]
    public void RecoilCatalogReconcilesNvidiaKillerRealtekSoftwareAndMissing()
    {
        var portal = new[]
        {
            Package("W11 Nvidia VGA.zip"),
            Package("W11 Intel WiFi.zip"),
            Package("W11 Realtek Audio.zip"),
            Package("W11 Control Centre.msi"),
            Package("W11 Intel ME.zip"),
            Package("W11 Intel Bluetooth.zip")
        };
        var local = new[]
        {
            Checked("w11 nvidia vga.ZIP", real: 1),
            Checked("W11 Intel WiFi.zip", weak: 1),
            Checked("W11 Realtek Audio.zip"),
            PcSpecialistPackageReconciliation.LocalSource
                .Unsupported("W11 Control Centre.msi"),
            Checked("W11 Intel Bluetooth.zip", real: 1),
            Checked("W11 Intel Bluetooth.zip", real: 0)
        };
        var report = PcSpecialistPackageReconciliation.Compare(portal, local);
        Assert.Equal(6, report.Rows.Count);
        Assert.Equal(3, report.Examined);
        Assert.Equal(1, report.WithPnPEvidence);
        Assert.Equal(1, report.ReviewOnly);
        Assert.Equal(1, report.Unsupported);
        Assert.Equal(1, report.Ambiguous);
        Assert.Equal(1, report.NotObserved);

        Assert.Equal(PcSpecialistPackageReconciliation.State.DeclaredPnpMatch,
            report.Rows[0].Status);
        Assert.Equal(PcSpecialistPackageReconciliation.State.ReviewOnlyInf,
            report.Rows[1].Status);
        Assert.Equal(
            PcSpecialistPackageReconciliation.State.ExaminedWithoutVisibleMatch,
            report.Rows[2].Status);
        Assert.Equal(PcSpecialistPackageReconciliation.State.UnsupportedFormat,
            report.Rows[3].Status);
        Assert.Equal(PcSpecialistPackageReconciliation.State.NotObserved,
            report.Rows[4].Status);
        Assert.Equal(PcSpecialistPackageReconciliation.State.AmbiguousCopies,
            report.Rows[5].Status);

        Assert.Contains("Hardware ID/Compatible ID dichiarato", report.Rows[0].Detail);
        Assert.Contains("SOLO revisione manuale", report.Rows[1].Detail);
        Assert.Contains("non prova assenza", report.Rows[2].Detail);
        Assert.Contains("non analizzato", report.Rows[3].Detail);
        Assert.Contains("Non osservato", report.Rows[4].Detail);
        Assert.Contains("NON attribuito", report.Rows[5].Detail);
        Assert.All(report.Rows, row =>
        {
            Assert.False(row.IsInstallable);
            Assert.False(row.IsOfficiallyVerified);
            Assert.False(row.IsWindowsApplicable);
        });
        Assert.False(report.IsInstallable);
        Assert.False(report.IsVerifiedOfficial);
        Assert.False(report.IsComplete);
        Assert.DoesNotContain("PRIVATE", report.Summary);
        Assert.DoesNotContain("SUBSYS_", report.Summary);
        Assert.DoesNotContain("W11 Nvidia VGA.zip", report.Summary);
        Assert.DoesNotContain("Installare", report.Summary);
    }

    [Fact]
    public void StrongInfEvidenceWinsOverReviewHintAndVersionsDoNotAuthorizeUpdate()
    {
        var p = Package("W11 Nvidia VGA.zip");
        var result = PcSpecialistPackageReconciliation.Compare([p],
            [Checked(p.Name, real: 1, weak: 5, partial: true)]);
        var row = Assert.Single(result.Rows);
        Assert.Equal(PcSpecialistPackageReconciliation.State.DeclaredPnpMatch,
            row.Status);
        Assert.Contains("Analisi parziale", row.Detail);
        Assert.Contains("NON verificate", row.Detail);
        Assert.False(row.IsInstallable);
        Assert.False(row.IsOfficiallyVerified);
    }

    [Fact]
    public void ReportFromArchiveDistinguishesRealAndWeakEvidenceAndGaps()
    {
        var evidence = new LocalOemDriverComparison.Report(
            3, 2,
            [
                new LocalOemDriverComparison.Finding(
                    "NVIDIA GPU", "nv.inf", "31.0", "99.0",
                    2, InfPlatformPreflight.Verdict.ModelSectionPotentialMatch,
                    LocalOemDriverComparison.VersionRelation.NewerHint),
                new LocalOemDriverComparison.Finding(
                    "Generic HDAUDIO", "audio.inf", "1.0", "4.0",
                    0, InfPlatformPreflight.Verdict.ManualReviewRequired,
                    LocalOemDriverComparison.VersionRelation.Unavailable)
            ],
            0, [], RejectedInfCount: 1);

        var found = PcSpecialistPackageReconciliation.LocalSource.FromArchive(
            "W11 Nvidia VGA.zip", evidence);
        Assert.True(found.Examined);
        Assert.Equal(1, found.PnpMatches);
        Assert.Equal(1, found.ReviewOnlyMatches);
        Assert.True(found.Partial);

        var r = PcSpecialistPackageReconciliation.Compare(
            [Package(found.FileName)], [found]);
        Assert.Equal(PcSpecialistPackageReconciliation.State.DeclaredPnpMatch,
            Assert.Single(r.Rows).Status);
        Assert.False(r.IsInstallable);
    }

    [Fact]
    public void ANameFoundOnlyInAnotherDirectoryIsAmbiguousNotLastWriterWins()
    {
        var file = Package("W11 Intel WiFi.zip");
        var r = PcSpecialistPackageReconciliation.Compare(
            [file],
            [
                Checked("W11 Intel WiFi.zip", real: 1),
                PcSpecialistPackageReconciliation.LocalSource
                    .Unsupported("w11 intel wifi.ZIP")
            ]);
        Assert.Equal(1, r.Ambiguous);
        Assert.Equal(0, r.WithPnPEvidence);
        Assert.Contains("NON attribuito", Assert.Single(r.Rows).Detail);
    }

    [Fact]
    public void ExactBasenameOnlyNeverGuessesDeviceOrVendorBySubstring()
    {
        var list = new[]
        {
            Package("W11 Nvidia VGA.zip"),
            Package("W11 Intel WiFi.zip")
        };
        var r = PcSpecialistPackageReconciliation.Compare(list,
        [
            Checked("W11 Nvidia VGA new.zip", real: 2),
            Checked("W11 Intel WiFi (1).zip", real: 1)
        ]);
        Assert.Equal(2, r.NotObserved);
        Assert.All(r.Rows, row =>
            Assert.Equal(PcSpecialistPackageReconciliation.State.NotObserved,
                row.Status));
    }

    [Fact]
    public void MalformedInputAndPrivateOrderAreNeverEchoed()
    {
        foreach (var file in new[]
        {
            "PRIVATE_SERIAL_001.zip",
            "W11 token-PRIVATE.zip",
            @"C:\PRIVATE_ORDER_1122\W11 Nvidia VGA.zip",
            "W11/Realtek.zip",
            "W11 Device\u202E.zip"
        })
        {
            Assert.Throws<InvalidDataException>(() =>
                PcSpecialistPackageReconciliation.Compare(
                    [Package(file)], []));
        }
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.Compare(
                [Package("W11 Nvidia VGA.zip"),
                 Package("w11 nvidia vga.ZIP")], []));
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.Compare(
                [Package("W11 Nvidia VGA.zip")],
                [Checked(@"C:\PRIVATE_ORDER\W11 Nvidia VGA.zip", real: 1)]));
        Assert.Throws<InvalidDataException>(() =>
            PcSpecialistPackageReconciliation.Compare(
                [Package("W11 Nvidia VGA.zip")],
                [Checked("W11 Nvidia VGA.zip", real: -1)]));
    }

    [Fact]
    public void UnsupportedFileNamesRemainOnlyInMemoryInPlanAndSummaryNeverLeaksThem()
    {
        var root = Directory.CreateTempSubdirectory("OmegaDrive-PRIVATE_ORDER_8833-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Control Centre.msi"),
                [0x4D, 0x53, 0x49, 0x01]);
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Firmware.7z"),
                [0x37, 0x7A, 0xBC, 0xAF]);
            // The planner sees one supported archive without opening it.
            File.WriteAllBytes(Path.Combine(root.FullName, "W11 Nvidia VGA.zip"),
                [0x50, 0x4B, 0x03, 0x04]);
            var plan = PcSpecialistLocalCatalogPlan.Create(root.FullName);
            Assert.Equal(2, plan.UnsupportedPackages);
            Assert.Equal(2, plan.UnsupportedPackageNames.Count);
            Assert.Contains("W11 Control Centre.msi",
                plan.UnsupportedPackageNames);
            Assert.Contains("W11 Firmware.7z",
                plan.UnsupportedPackageNames);
            Assert.DoesNotContain("PRIVATE_ORDER", string.Join(" ",
                plan.UnsupportedPackageNames));

            var report = PcSpecialistPackageReconciliation.Compare(
                [Package("W11 Control Centre.msi"),
                 Package("W11 Firmware.7z")],
                plan.UnsupportedPackageNames.Select(
                    PcSpecialistPackageReconciliation.LocalSource
                        .Unsupported).ToArray());
            Assert.Equal(2, report.Unsupported);
            Assert.DoesNotContain("PRIVATE_ORDER", report.Summary);
            Assert.DoesNotContain(root.FullName, report.Summary);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
