using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #473: the Dell/HP automatic metadata retry must never erase a
/// manufacturer-assessed update from a different, opt-in OEM companion.
/// </summary>
public sealed class AutomaticOemCatalogRowReplacementTests
{
    private static VendorSourceCheck Row(string title, string vendor,
        string source, bool metadata = false, bool applicable = false,
        bool release = false, int manualDevices = 0) =>
        new(title, vendor, "Non confrontata",
            vendor == "Dell" ? "https://www.dell.com/support/home" :
            vendor == "HP" ? "https://support.hp.com/" :
            VendorSourceDiscovery.NvidiaDriversUrl,
            source, "Sola lettura", 0,
            IsCatalogMetadata: metadata,
            IsVendorApplicable: applicable,
            IsReleaseAnnouncement: release)
        { GroupedManualDevices = manualDevices };

    private static VendorSourceCheck DellCatalog(string title = "Dell pack") =>
        Row(title, "Dell", "Dell Driver Pack Catalog", metadata: true);

    private static VendorSourceCheck HpCatalog(string title = "HP SoftPaq") =>
        Row(title, "HP", "HP Client Management Script Library", metadata: true);

    private static VendorSourceCheck DellDcu() =>
        Row("Aggiornamento Dell DCU", "Dell",
            "Dell Command Update · scansione per PC corrente",
            metadata: true, applicable: true);

    private static VendorSourceCheck HpHpia() =>
        Row("Aggiornamento HP HPIA", "HP",
            "HP Image Assistant · Analyze / Drivers / List",
            metadata: true, applicable: true);

    [Fact]
    public void RetryDellReplacesOnlyDellDeploymentMetadata()
    {
        var old = DellCatalog("Pack precedente");
        var dcu = DellDcu();
        var hp = HpCatalog();
        var manual = Row("Supporto Dell", "Dell", "Supporto manuale",
            manualDevices: 3);
        var otherDellMetadata = Row("Dell altra fonte", "Dell",
            "Dell catalogo avanzato", metadata: true);
        var announcement = Row("Notizia Dell", "Dell",
            "Dell Driver Pack Catalog", metadata: true, release: true);
        var newPack = DellCatalog("Pack riletto");

        var results = AutomaticOemCatalogRowReplacement.ForRetry(
            [old, dcu, hp, manual, otherDellMetadata, announcement],
            "Dell", [newPack]);

        Assert.Equal([dcu, hp, manual, otherDellMetadata, announcement, newPack],
            results);
        Assert.True(results[0].IsVendorApplicable);
        Assert.All(results, result => Assert.False(result.IsInstallable));
    }

    [Fact]
    public void RetryHpKeepsHpiaRecommendationsAndOtherVendorsOnFailure()
    {
        var previous = HpCatalog("Old CMSL");
        var hpia = HpHpia();
        var dell = DellDcu();
        var unrelated = DellCatalog();
        var oldHpManual = Row("Manual HP", "HP", "Supporto HP",
            manualDevices: 2);

        // A failed metadata query returns no rows. It does NOT invalidate
        // an independent, previously consented HPIA assessment.
        var results = AutomaticOemCatalogRowReplacement.ForRetry(
            [previous, hpia, dell, unrelated, oldHpManual], "HP", []);

        Assert.Equal([hpia, dell, unrelated, oldHpManual], results);
        Assert.Equal(2, results.Count(row => row.IsVendorApplicable));
    }

    [Fact]
    public void RetryHpSuccessReplacesOldCmslWithoutDuplicatingRows()
    {
        var applicable = HpHpia();
        var first = HpCatalog("Old");
        var second = HpCatalog("New");
        var initial = new[] { first, applicable };

        var once = AutomaticOemCatalogRowReplacement.ForRetry(
            initial, "HP", [second]);
        var twice = AutomaticOemCatalogRowReplacement.ForRetry(
            once, "HP", [second]);

        Assert.Equal([applicable, second], once);
        Assert.Equal(once, twice);
        Assert.Single(twice, row => row.IsVendorApplicable);
    }

    [Fact]
    public void RetryDoesNotAcceptAnUnrelatedCompanionAsAutomaticMetadata()
    {
        var existing = new[] { HpCatalog(), HpHpia() };
        Assert.Throws<InvalidDataException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(
                existing, "HP", [HpHpia()]));
        Assert.Equal(2, existing.Length);
    }

    [Fact]
    public void RetryRejectsWrongVendorSourceAndOversizedResults()
    {
        var old = new[] { DellCatalog(), DellDcu() };
        Assert.Throws<ArgumentException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(old, "Lenovo", []));
        Assert.Throws<InvalidDataException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(old, "Dell",
                [HpCatalog()]));
        Assert.Throws<InvalidDataException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(old, "Dell",
                [Row("Wrong source", "Dell", "Dell Command Update",
                    metadata: true)]));
        Assert.Throws<InvalidDataException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(old, "Dell",
                Enumerable.Range(0, 121).Select(i =>
                    DellCatalog("Pack " + i))));
    }

    [Fact]
    public void RetryValidatesInputsBeforeDiscardingEvidence()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(
                null!, "Dell", []));
        Assert.Throws<ArgumentNullException>(() =>
            AutomaticOemCatalogRowReplacement.ForRetry(
                [DellDcu()], "Dell", null!));
    }
}
