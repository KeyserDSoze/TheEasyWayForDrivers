using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #468: the Home badge and screen reader actions must reflect the
/// SAME eligibility policy used to install WUA drivers. The UI text
/// must never manufacture a recommendation or expose extra PnP IDs.
/// </summary>
public sealed class CompactUpdatePresentationAccessibilityTests
{
    private const string Nvidia = @"PCI\VEN_10DE&DEV_28A0";
    private const string ValidGuid = "00000000-0000-4000-8000-000000000468";

    private static DriverUpdateInfo Offer(
        string title = "NVIDIA Display Driver",
        string id = ValidGuid, string source = "Windows Update online",
        bool hidden = false, bool advanced = false,
        bool optional = false, string cls = "Display") =>
        new(id, title, null, cls, "Microsoft", null, null, false,
            "NVIDIA", "RTX 4060", Nvidia, null,
            IsHidden: hidden, IsOptional: optional,
            IsAdvancedCandidate: advanced, SearchSource: source);

    [Fact]
    public void OrdinaryRealWuaDriverGetsConsigliatoAndUniqueActionNames()
    {
        var update = Offer();
        Assert.True(QuickUpdatePolicy.IsEligible(update));
        Assert.Equal("Consigliato",
            CompactUpdatePresentation.KindText(update));
        Assert.Equal("Seleziona NVIDIA Display Driver per aggiornare tramite Windows Update",
            CompactUpdatePresentation.SelectionAccessibleName(update));
        Assert.Equal("Aggiorna NVIDIA Display Driver tramite Windows Update",
            CompactUpdatePresentation.ActionAccessibleName(update));
        Assert.DoesNotContain(ValidGuid,
            CompactUpdatePresentation.ActionAccessibleName(update));
        Assert.DoesNotContain(Nvidia,
            CompactUpdatePresentation.SelectionAccessibleName(update));
        Assert.DoesNotContain(update.SearchSource,
            CompactUpdatePresentation.SelectionAccessibleName(update));
        Assert.True(CompactResultFilter.IsVisible(update, "recommended"));
        Assert.Single(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Theory]
    [InlineData("NVIDIA GPU Firmware Update", "Display", "Firmware / BIOS")]
    [InlineData("NVIDIA Display Driver", "Firmware", "Firmware / BIOS")]
    [InlineData("Intel UEFI capsule", "System", "Firmware / BIOS")]
    [InlineData("NVIDIA BIOS Driver", "Display", "Firmware / BIOS")]
    public void FirmwareSafetyVetoNeverGetsRecommendedBadgeOrInstallAction(
        string title, string cls, string expected)
    {
        var update = Offer(title: title, cls: cls);
        Assert.Equal(expected, CompactUpdatePresentation.KindText(update));
        Assert.False(QuickUpdatePolicy.IsEligible(update));
        Assert.False(CompactResultFilter.IsVisible(update, "recommended"));
        Assert.True(CompactResultFilter.IsVisible(update, "advanced"));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([update]));
        Assert.StartsWith("Apri dettagli",
            CompactUpdatePresentation.ActionAccessibleName(update));
        Assert.Contains("nessuna installazione rapida",
            CompactUpdatePresentation.ActionAccessibleName(update));
        Assert.Contains("Selezione non disponibile",
            CompactUpdatePresentation.SelectionAccessibleName(update));
    }

    [Fact]
    public void OptionalBrowseOnlyCannotBePresentedAsOrdinary()
    {
        var update = Offer(optional: true);
        Assert.Equal("Facoltativo", CompactUpdatePresentation.KindText(update));
        Assert.False(QuickUpdatePolicy.IsEligible(update));
        Assert.True(CompactResultFilter.IsVisible(update, "optional"));
        Assert.False(CompactResultFilter.IsVisible(update, "recommended"));
        Assert.Contains("solo dettagli",
            CompactUpdatePresentation.SelectionAccessibleName(update));
        Assert.Contains("nessuna installazione rapida",
            CompactUpdatePresentation.ActionAccessibleName(update));
    }

    [Theory]
    [InlineData("Nascosto", true, false)]
    [InlineData("Avanzato", false, true)]
    public void AdvancedOrHiddenHaveUnambiguousReviewOnlyStatus(
        string badge, bool hidden, bool advanced)
    {
        var update = Offer(hidden: hidden, advanced: advanced);
        Assert.Equal(badge, CompactUpdatePresentation.KindText(update));
        Assert.False(QuickUpdatePolicy.IsEligible(update));
        Assert.True(CompactResultFilter.IsVisible(update, "advanced"));
        Assert.False(CompactResultFilter.IsVisible(update, "recommended"));
        Assert.StartsWith("Apri dettagli",
            CompactUpdatePresentation.ActionAccessibleName(update));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([update]));
    }

    [Theory]
    [InlineData("NVIDIA vendor site", ValidGuid)]
    [InlineData("Unverified OEM mirror", ValidGuid)]
    [InlineData("Windows Update online", "PRIVATE_INVALID_GUID")]
    [InlineData("Microsoft Update online · avanzato", ValidGuid)]
    public void UnknownOriginOrIdentityCannotBeMislabelledConsigliato(
        string source, string id)
    {
        var update = Offer(source: source, id: id);
        Assert.Equal("Da verificare",
            CompactUpdatePresentation.KindText(update));
        Assert.False(QuickUpdatePolicy.IsEligible(update));
        Assert.False(CompactResultFilter.IsVisible(update, "recommended"));
        Assert.True(CompactResultFilter.IsVisible(update, "advanced"));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds([update]));
        Assert.Contains("non disponibile",
            CompactUpdatePresentation.SelectionAccessibleName(update));
        Assert.StartsWith("Apri dettagli",
            CompactUpdatePresentation.ActionAccessibleName(update));
    }

    [Fact]
    public void EveryRecommendedBadgeMustPassExactlyTheInstallPolicy()
    {
        var cases = new[]
        {
            Offer(), Offer(source: "Windows configurato"),
            Offer(source: "Microsoft Update online"),
            Offer(hidden: true), Offer(advanced: true),
            Offer(optional: true), Offer(optional: true, hidden: true),
            Offer(title: "UEFI Firmware"),
            Offer(title: "BIOS 1.01", optional: true),
            Offer(source: "OEM website"), Offer(id: "not-a-guid"),
            Offer(source: "Microsoft Update online · avanzato"),
            Offer(cls: "Firmware"), Offer(source: "Windows Update")
        };
        foreach (var item in cases)
        {
            Assert.Equal(QuickUpdatePolicy.IsEligible(item),
                CompactUpdatePresentation.KindText(item) == "Consigliato");
            Assert.Equal(QuickUpdatePolicy.IsEligible(item),
                CompactUpdatePresentation.ActionAccessibleName(item)
                    .StartsWith("Aggiorna ", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(@"NVIDIA Display C:\PRIVATE_ORDER\driver.inf")]
    [InlineData("GPU 00000000-0000-4000-8000-000000000468")]
    [InlineData("NVIDIA Display \u202E secret")]
    [InlineData("NVIDIA Display\nPRIVATE_SERIAL")]
    [InlineData("")]
    public void MalformedWuaTitlesDoNotLeakIntoExtraUiAutomationFields(
        string title)
    {
        var update = Offer(title: title);
        var accessible = CompactUpdatePresentation.ActionAccessibleName(update);
        Assert.Contains("driver Windows senza nome verificato", accessible);
        Assert.DoesNotContain("PRIVATE_", accessible);
        Assert.DoesNotContain(ValidGuid, accessible);
        Assert.DoesNotContain(Nvidia, accessible);
        Assert.DoesNotContain("secret", accessible);
    }

    [Fact]
    public void LongButValidTitleIsBoundedForScreenReaderUsability()
    {
        var title = "NVIDIA " + new string('A', 280);
        var label = CompactUpdatePresentation.ActionAccessibleName(
            Offer(title: title));
        Assert.StartsWith("Aggiorna NVIDIA ", label);
        Assert.Contains("… tramite Windows Update", label);
        Assert.True(label.Length < 190);
        Assert.DoesNotContain(ValidGuid, label);
    }
}
