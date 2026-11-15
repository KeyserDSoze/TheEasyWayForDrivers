using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class CompactInstallActionPolicyTests
{
    private static DriverUpdateInfo Update(string id,
        bool optional = false, bool hidden = false, bool advanced = false,
        string title = "Intel - Display", string source = "Windows Update online") =>
        new(id, title, null, null, "Microsoft", null, null, false,
            null, null, null, null, hidden, optional, advanced)
        { SearchSource = source };

    [Fact]
    public void SelectedSubsetCannotIgnoreOptionalVetoOnSameWuaGuid()
    {
        var id = Guid.NewGuid();
        var ordinary = Update(id.ToString("D"));
        var optional = Update(id.ToString("B").ToUpperInvariant(), optional: true);
        Assert.Empty(CompactInstallActionPolicy.SelectIds(
            [ordinary, optional], [ordinary]));
        Assert.Single(CompactInstallActionPolicy.SelectIds(
            [ordinary], [ordinary]));
    }

    [Fact]
    public void HiddenFirmwareVetoSurvivesFilteredSingleRow()
    {
        var id = Guid.NewGuid();
        var ordinary = Update(id.ToString("N"));
        var veto = Update(id.ToString("P"), title: "UEFI BIOS Firmware",
            hidden: true);
        Assert.Empty(CompactInstallActionPolicy.SelectIds(
            [ordinary, veto], [ordinary]));
        Assert.Empty(CompactInstallActionPolicy.SelectIds(
            [veto], [veto]));
    }

    [Fact]
    public void ApprovedSelectionIgnoresHiddenRowsAndNormalizesAllGuidFormats()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var ordinaryA = Update(idA.ToString("N"));
        var ordinaryB = Update(idB.ToString("B"));
        var filteredSelection = new[]
        {
            ordinaryA,
            Update(idA.ToString("P")),
            Update(idB.ToString("D"), source: "NVIDIA official")
        };
        Assert.Equal([idA.ToString("D")], CompactInstallActionPolicy.SelectIds(
            [ordinaryA, ordinaryB], filteredSelection));
        Assert.Equal(
            [idB.ToString("D"), idA.ToString("D")],
            CompactInstallActionPolicy.SelectIds(
                [ordinaryA, ordinaryB], [ordinaryB, ordinaryA]));
    }

    [Fact]
    public void ForeignSourceCannotVetoTrustedWindowsUpdateOrAuthorizeNewId()
    {
        var id = Guid.NewGuid();
        var wua = Update(id.ToString("D"));
        var foreignFirmware = Update(id.ToString("B"), optional: true,
            title: "Firmware", source: "Unknown OEM source");
        Assert.Equal([id.ToString("D")],
            CompactInstallActionPolicy.SelectIds([wua, foreignFirmware], [wua]));
        Assert.Empty(CompactInstallActionPolicy.SelectIds(
            [wua], [foreignFirmware]));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    public void FinalIpcAuthorizationCanonicalizesEquivalentGuidFormats(string format)
    {
        var id = Guid.NewGuid();
        var allowed = new[] { id.ToString("D") };
        Assert.True(CompactInstallActionPolicy.TryAuthorizeRequested(
            allowed, [id.ToString(format)], out var canonical));
        Assert.Equal([id.ToString("D")], canonical);
    }

    [Fact]
    public void FinalIpcAuthorizationRejectsDuplicateRepresentationsAndUnknownIds()
    {
        var id = Guid.NewGuid();
        var allowed = new[] { id.ToString("D") };
        Assert.False(CompactInstallActionPolicy.TryAuthorizeRequested(
            allowed, [id.ToString("D"), id.ToString("B")], out var duplicated));
        Assert.Empty(duplicated);
        Assert.False(CompactInstallActionPolicy.TryAuthorizeRequested(
            allowed, [Guid.NewGuid().ToString("D")], out var unknown));
        Assert.Empty(unknown);
        Assert.False(CompactInstallActionPolicy.TryAuthorizeRequested(
            allowed, ["not-a-wua-guid"], out var invalid));
        Assert.Empty(invalid);
        Assert.False(CompactInstallActionPolicy.TryAuthorizeRequested(
            allowed, [], out var empty));
        Assert.Empty(empty);
    }

    [Fact]
    public void SelectedOptionalAdvancedAndFirmwareCannotRequestAnOrdinaryGuid()
    {
        var id = Guid.NewGuid();
        var safe = Update(id.ToString("D"));
        Assert.Empty(CompactInstallActionPolicy.SelectIds([safe],
            [Update(id.ToString("B"), optional: true)]));
        Assert.Empty(CompactInstallActionPolicy.SelectIds([safe],
            [Update(id.ToString("B"), advanced: true)]));
        Assert.Empty(CompactInstallActionPolicy.SelectIds([safe],
            [Update(id.ToString("B"), title: "BIOS Firmware")]));
    }

    [Fact]
    public void DisplayCanBecomeReviewOnlyWithoutLeakingUpdateOrDeviceIdentifiers()
    {
        var safe = Update(Guid.NewGuid().ToString("D"));
        Assert.Equal("Consigliato", CompactUpdatePresentation.KindText(safe, true));
        Assert.Equal("Da verificare", CompactUpdatePresentation.KindText(safe, false));
        Assert.StartsWith("Apri dettagli", CompactUpdatePresentation.ActionAccessibleName(safe, false));
        Assert.StartsWith("Selezione non disponibile",
            CompactUpdatePresentation.SelectionAccessibleName(safe, false));
        Assert.DoesNotContain(safe.Id, CompactUpdatePresentation.ActionAccessibleName(safe, false));
    }
}
