using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// Real-world SNDW codecs can append an OEM SUBSYS token after VER/UID.
/// Keep the complete original ID for exact WUA/INF correlation but strip
/// the OEM variant from the manual, browser-only Catalog model search.
/// No result here authorizes a driver installation.
/// </summary>
public sealed class SndwSubsystemIdentityTests
{
    private const string Full =
        @"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028";
    private const string PublicModel =
        @"SNDW\CLASS_00&MAN_025D&PART_0711";

    private static DriverInfo Device(string id) =>
        new("PRIVATE_PNP_INSTANCE", "Generic audio codec", "Microsoft",
            "MEDIA", "Microsoft", "1.0", null, "oem42.inf",
            true, true, 0, [id], []);

    private static DriverUpdateInfo Wua(string id) =>
        new("00000000-0000-4000-8000-000000000360",
            "Windows codec driver", null, "MEDIA", "Microsoft",
            null, null, false, "Microsoft", null, id, null,
            SearchSource: "Windows Update online");

    private static string Inf(string id) =>
        "[Version]\nSignature=\"$Windows NT$\"\n" +
        "[Manufacturer]\n%Vendor%=Models,NTamd64\n" +
        "[Models.NTamd64]\n%Codec%=Install," + id + "\n";

    [Theory]
    [InlineData(Full)]
    [InlineData(@"sndw\class_00&man_025d&part_0711&ver_02&uid_00&subsys_09831028")]
    public void RealSndwSubsystemIsExactWuaAndInfEvidenceButOnlyPublicCatalogQuery(
        string full)
    {
        Assert.True(ModernAudioPnpMatchPolicy.IsSpecificHardwareId(full));
        Assert.True(DriverUpdateMatcher.IsCorrelatableHardwareId(full));
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery([full]));

        var device = Device(full);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(device, Wua(full)));
        Assert.Equal(2, InfModelEvidence.MatchStrength(device,
            InfModelEvidence.ReadDeclaredIds(Inf(full))));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(device,
            Wua(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_098F1028")));

        var target = Assert.IsType<MicrosoftUpdateCatalogSearch.Target>(
            MicrosoftUpdateCatalogSearch.Create(device));
        Assert.Equal(PublicModel, target.Query);
        Assert.Equal(PublicModel,
            MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(full));
        Assert.Equal("q=" + PublicModel,
            Uri.UnescapeDataString(target.Url.Query.TrimStart('?')));
        Assert.Equal(Uri.UriSchemeHttps, target.Url.Scheme);
        Assert.Equal(MicrosoftUpdateCatalogSearch.CatalogHost, target.Url.Host);
        Assert.False(target.FromCompatibleId);
        Assert.False(target.CatalogWasQueried);
        Assert.False(target.IsInstallable);
        Assert.DoesNotContain("SUBSYS", target.Url.AbsoluteUri);
        Assert.DoesNotContain("VER_", target.Url.AbsoluteUri);
        Assert.DoesNotContain("UID_", target.Url.AbsoluteUri);
        Assert.DoesNotContain("PRIVATE", target.Url.AbsoluteUri);

        var unchanged = PnpSetupApiIdentity.Merge([full], [],
            [@"SNDW\CLASS_00&MAN_0242&PART_0711"], [], null, null);
        Assert.Equal(full, Assert.Single(unchanged.HardwareIds));
        Assert.False(unchanged.UsedSetupApi);
    }

    [Fact]
    public void SameInstanceNativeOrSignedModelRecoversGenericWmiScope()
    {
        var generic = @"SNDW\CLASS_00&MAN_025D";
        Assert.True(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [generic]));

        var native = PnpSetupApiIdentity.Merge([generic], [], [Full],
            [], null, null);
        Assert.Equal(Full, native.HardwareIds[0]);
        Assert.Contains(generic, native.HardwareIds);
        Assert.True(native.UsedSetupApi);
        Assert.False(native.UsedSignedDriver);
        Assert.Equal(2, DriverUpdateMatcher.MatchStrength(
            Device(native.HardwareIds[0]), Wua(Full)));

        var signed = PnpSetupApiIdentity.Merge([generic], [], [],
            [], Full, null);
        Assert.Equal(Full, signed.HardwareIds[0]);
        Assert.True(signed.UsedSignedDriver);
        Assert.False(signed.UsedSetupApi);

        var differentManufacturer = PnpSetupApiIdentity.Merge(
            [generic], [],
            [@"SNDW\CLASS_00&MAN_0242&PART_0711&VER_02&UID_00&SUBSYS_09831028"],
            [], null, null);
        Assert.Equal(generic, Assert.Single(differentManufacturer.HardwareIds));
        Assert.False(differentManufacturer.UsedSetupApi);
    }

    [Theory]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&SUBSYS_09831028")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&SUBSYS_09831028")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&UID_00&SUBSYS_09831028")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_0983102")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_098310280")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_0983102G")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028&FAKE_01")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028&SUBSYS_098F1028")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028\PRIVATE")]
    [InlineData(@"SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00&SUBSYS_09831028" + "\n")]
    public void MalformedOrPrivateSubsystemNeverCreatesMatchOrCatalogLink(
        string id)
    {
        Assert.False(ModernAudioPnpMatchPolicy.IsSpecificHardwareId(id));
        Assert.False(DriverUpdateMatcher.IsCorrelatableHardwareId(id));
        Assert.Null(MicrosoftUpdateCatalogSearch.TryGetPublicModelKey(id));
        Assert.Null(MicrosoftUpdateCatalogSearch.Create(Device(id)));
        Assert.Equal(0, DriverUpdateMatcher.MatchStrength(Device(id), Wua(id)));
        Assert.False(PnpSetupApiIdentity.NeedsSpecificHardwareModelRecovery(
            [id]));
    }
}
