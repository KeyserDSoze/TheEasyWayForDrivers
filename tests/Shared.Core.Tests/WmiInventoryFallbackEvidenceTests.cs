using System.Text.Json;
using System.Text.Json.Nodes;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WmiInventoryFallbackEvidenceTests
{
    private const string PnpHardware = @"PCI\VEN_10DE&DEV_28A0";
    private const string PrivateInstance = @"PCI\VEN_10DE&DEV_28A0\PRIVATE-SERIAL-123";

    private static DriverInfo Device(bool signedQuerySucceeded = false,
        uint windowsProblem = 0, bool hasDriver = false) =>
        new(PrivateInstance, "Microsoft Basic Display Adapter",
            "Microsoft", "Display", null, null, null, null, false,
            hasDriver, windowsProblem, [PnpHardware], [],
            DriverMetadataQuerySucceeded: signedQuerySucceeded);

    [Fact]
    public void MissingSignedDriverMetadataMustNotCreateFalseAttention()
    {
        var device = Device();
        Assert.False(device.DriverMetadataQuerySucceeded);
        Assert.False(device.HasDriver);
        Assert.False(device.NeedsAttention);
        Assert.True(device.IsDriverUnverified);
        Assert.Equal("Da verificare", device.Status);
        Assert.Contains("non verificati", device.ProblemExplanation);
        Assert.DoesNotContain("mancante", device.Status);
        Assert.DoesNotContain("aggiornato", device.Status);
    }

    [Theory]
    [InlineData(28U, "Driver mancante")]
    [InlineData(43U, "Errore Windows")]
    public void PnpProblemCodesRemainAuthoritativeWithoutSignedMetadata(
        uint errorCode, string expectedStatus)
    {
        var device = Device(windowsProblem: errorCode);
        Assert.True(device.NeedsAttention);
        Assert.True(device.HasWindowsProblem);
        Assert.Equal(expectedStatus, device.Status);
        Assert.Equal(errorCode == 28, device.IsDriverMissing);
    }

    [Fact]
    public void NormalCompletedSignedMetadataQueryKeepsCurrentSemantics()
    {
        var noMetadataForOneDevice = Device(signedQuerySucceeded: true);
        Assert.True(noMetadataForOneDevice.NeedsAttention);
        Assert.Equal("Da verificare", noMetadataForOneDevice.Status);

        var withDriver = Device(signedQuerySucceeded: true, hasDriver: true);
        Assert.False(withDriver.NeedsAttention);
        Assert.Equal("OK", withDriver.Status);
    }

    [Fact]
    public void NvidiaHardwareRemainsDiscoverableWhenDriverVersionsUnavailable()
    {
        var device = Device();
        var rows = VendorSourceDiscovery.Find([device], []);
        var row = Assert.Single(rows);
        Assert.Equal("NVIDIA", row.Vendor);
        Assert.Equal(0, row.WuaMatches);
        Assert.False(row.IsVendorApplicable);
        Assert.Contains("nessun aggiornamento OEM confermato", row.StatusText);
    }

    [Fact]
    public void AggregatedReportMarksIncompleteDriverMetadataWithoutIdentifiers()
    {
        var device = Device();
        var output = ScanCoverageText.Create(null, [],
            1, 0, 0, [device], inventoryComplete: true);
        Assert.Contains("Metadati driver installati non verificati: 1", output);
        Assert.Contains("non significa driver mancanti", output);
        Assert.DoesNotContain("PRIVATE-SERIAL-123", output);
        Assert.DoesNotContain(PnpHardware, output);
        Assert.DoesNotContain("Microsoft Basic Display Adapter", output);
    }

    [Fact]
    public void OldIpcJsonWithoutNewFlagDeserializesAsNormalCompletedQuery()
    {
        var expected = Device(signedQuerySucceeded: true);
        var legacy = JsonNode.Parse(JsonSerializer.Serialize(expected))!.AsObject();
        Assert.True(legacy.Remove(nameof(DriverInfo.DriverMetadataQuerySucceeded)));

        var deserialized = JsonSerializer.Deserialize<DriverInfo>(legacy.ToJsonString());
        Assert.NotNull(deserialized);
        Assert.True(deserialized.DriverMetadataQuerySucceeded);
        Assert.Equal(expected.DeviceId, deserialized.DeviceId);
        Assert.Equal(expected.HardwareIds, deserialized.HardwareIds);
    }

    [Fact]
    public void NewUnverifiedFlagSurvivesIpcRoundTrip()
    {
        var input = Device();
        var output = JsonSerializer.Deserialize<DriverInfo>(JsonSerializer.Serialize(input));
        Assert.NotNull(output);
        Assert.False(output.DriverMetadataQuerySucceeded);
        Assert.Equal("Da verificare", output.Status);
    }
}
