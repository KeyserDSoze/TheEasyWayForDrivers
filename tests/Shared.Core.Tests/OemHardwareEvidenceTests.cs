using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemHardwareEvidenceTests
{
    private static DriverInfo Device() => new("id", "Test device", "Intel",
        "System", "Intel", "1.0", null, null, true, true, 0,
        [@"PCI\VEN_8086&DEV_1234&SUBSYS_00000001"],
        [@"PCI\VEN_8086&DEV_1234"]);

    private static OemZipInfEvidence.Report Evidence(params string[] ids) =>
        new([new OemZipInfEvidence.InfEntry("drivers/a.inf",
            ids.Select(id => new InfModelEvidence.DeclaredId("Models.NTamd64", id)).ToArray(),
            "Dichiarazioni INF")], []);

    [Fact]
    public void PrefersExactHardwareIdOverCompatibleId()
    {
        var result = OemHardwareEvidence.Compare(Evidence(
            @"PCI\VEN_8086&DEV_1234&SUBSYS_00000001",
            @"PCI\VEN_8086&DEV_1234"), [Device()]);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(OemHardwareEvidence.Strength.HardwareId, result.Matches[0].Level);
        Assert.Equal(OemHardwareEvidence.Strength.CompatibleId, result.Matches[1].Level);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void SubstringOrDifferentIdentifierNeverMatches()
    {
        var result = OemHardwareEvidence.Compare(
            Evidence(@"PCI\VEN_8086&DEV_123", @"PCI\VEN_8086&DEV_1235"), [Device()]);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void EmptyEvidenceAndDeviceInventoryAreSafe()
    {
        Assert.Empty(OemHardwareEvidence.Compare(Evidence(), [Device()]).Matches);
        Assert.Empty(OemHardwareEvidence.Compare(
            Evidence(@"PCI\VEN_8086&DEV_1234"), []).Matches);
    }
}
