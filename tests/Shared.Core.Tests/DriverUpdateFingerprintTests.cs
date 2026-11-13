using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverUpdateFingerprintTests
{
    private static DriverUpdateInfo Update(
        string id,
        string source = "Windows Update online",
        bool optional = false, bool hidden = false, bool advanced = false,
        string title = "Windows driver", string driverClass = "Display") =>
        new(id, title, null, driverClass, "Microsoft", null, 100,
            false, "Microsoft", null, null, null,
            IsHidden: hidden, IsOptional: optional,
            IsAdvancedCandidate: advanced, SearchSource: source);

    [Fact]
    public void Create_IsOrderIndependent()
    {
        var a = Update("00000000-0000-4000-8000-000000000001");
        var b = Update("00000000-0000-4000-8000-000000000002");
        Assert.Equal(DriverUpdateFingerprint.Create([a, b]),
            DriverUpdateFingerprint.Create([b, a]));
        Assert.Equal(2, DriverUpdateFingerprint.SelectNotifiable([a, b]).Count);
    }

    [Fact]
    public void Create_DeduplicatesCanonicalGuidAcrossAllTextFormats()
    {
        var id = Guid.Parse("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE");
        var first = Update(id.ToString("B").ToUpperInvariant());
        var second = Update(id.ToString("N"));
        var third = Update(id.ToString("P"));
        var standard = Update(id.ToString("D"));
        Assert.Equal(id.ToString("D"), DriverUpdateFingerprint.Create(
            [first, second, third, standard]));
        Assert.Equal(DriverUpdateFingerprint.Create([standard]),
            DriverUpdateFingerprint.Create([third, first]));
        Assert.Single(DriverUpdateFingerprint.SelectNotifiable(
            [first, second, third, standard]));
    }

    [Fact]
    public void Create_ChangesWhenUpdateSetChanges()
    {
        var a = Update(Guid.NewGuid().ToString("D"));
        var b = Update(Guid.NewGuid().ToString("D"));
        Assert.NotEqual(DriverUpdateFingerprint.Create([a]),
            DriverUpdateFingerprint.Create([a, b]));
    }

    [Fact]
    public void TrustedBrowseOnlyCopyVetoesSameGuidButKeepsIndependentOffer()
    {
        var vetoed = Guid.NewGuid();
        var good = Guid.NewGuid();
        var normal = Update(vetoed.ToString("N"));
        var optional = Update(vetoed.ToString("B"),
            source: "Microsoft Update online", optional: true);
        var independent = Update(good.ToString("D"));

        Assert.Equal(good.ToString("D"), DriverUpdateFingerprint.Create(
            [normal, optional, independent]));
        Assert.Equal(independent, Assert.Single(
            DriverUpdateFingerprint.SelectNotifiable(
                [independent, optional, normal])));
        Assert.Equal("", DriverUpdateFingerprint.Create([normal, optional]));
    }

    [Theory]
    [InlineData("BIOS Update", "Display")]
    [InlineData("Firmware - Embedded Controller", "Display")]
    [InlineData("UEFI System", "Display")]
    [InlineData("Display driver", "Firmware")]
    public void FirmwareOrBiosCopyVetoesOrdinaryGuid(
        string title, string driverClass)
    {
        var id = Guid.NewGuid();
        var normal = Update(id.ToString("D"));
        var firmware = Update(id.ToString("P"),
            source: "Microsoft Update online · avanzato",
            advanced: true, title: title, driverClass: driverClass);

        Assert.Empty(DriverUpdateFingerprint.SelectNotifiable(
            [firmware, normal]));
        Assert.Equal("", DriverUpdateFingerprint.Create([normal, firmware]));
    }

    [Fact]
    public void UnknownOemSourceCannotVetoGenuineWuaGuid()
    {
        var id = Guid.NewGuid();
        var wua = Update(id.ToString("D"));
        var fake = Update(id.ToString("B"),
            source: "Untrusted OEM website", optional: true,
            title: "BIOS Firmware");

        Assert.Equal(wua.Id, DriverUpdateFingerprint.Create([fake, wua]));
        Assert.Equal(wua, Assert.Single(
            DriverUpdateFingerprint.SelectNotifiable([fake, wua])));
    }

    [Fact]
    public void AdvancedHiddenOptionalMalformedAndForeignOnlyNeverNotify()
    {
        var updates = new[]
        {
            Update(Guid.NewGuid().ToString("D"), hidden: true),
            Update(Guid.NewGuid().ToString("D"), optional: true),
            Update(Guid.NewGuid().ToString("D"), advanced: true,
                source: "Microsoft Update online · avanzato"),
            Update(Guid.NewGuid().ToString("D"), title: "BIOS Firmware"),
            Update(Guid.NewGuid().ToString("D"),
                source: "Untrusted vendor portal"),
            Update("malformed update id")
        };

        Assert.Empty(DriverUpdateFingerprint.SelectNotifiable(updates));
        Assert.Equal("", DriverUpdateFingerprint.Create(updates));
    }

    [Fact]
    public void NonNotifiableChangesDoNotChangeFingerprint()
    {
        var good = Update(Guid.NewGuid().ToString("D"));
        var advanced = Update(Guid.NewGuid().ToString("D"), advanced: true,
            source: "Windows Update online · avanzato");
        var hidden = Update(Guid.NewGuid().ToString("D"), hidden: true);
        Assert.Equal(DriverUpdateFingerprint.Create([good]),
            DriverUpdateFingerprint.Create([hidden, advanced, good]));
    }

    [Fact]
    public void SelectNotifiableValidatesArgument()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DriverUpdateFingerprint.Create(null!));
        Assert.Throws<ArgumentNullException>(() =>
            DriverUpdateFingerprint.SelectNotifiable(null!));
    }
}
