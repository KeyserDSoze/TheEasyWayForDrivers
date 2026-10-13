using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Security;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class OemInfArchitectureEvidenceTests
{
    private static OemZipInfEvidence.Report Evidence(string section) =>
        new([new OemZipInfEvidence.InfEntry("drivers/a.inf",
            [new InfModelEvidence.DeclaredId(section, @"PCI\VEN_8086&DEV_1234")],
            "Dichiarazione")], []);

    [Theory]
    [InlineData("Models.NTamd64", "x64", OemInfArchitectureEvidence.Verdict.ArchitectureMatches)]
    [InlineData("Models.NTarm64", "x64", OemInfArchitectureEvidence.Verdict.ArchitectureDiffers)]
    [InlineData("Models.NTx86", "ARM64", OemInfArchitectureEvidence.Verdict.ArchitectureDiffers)]
    [InlineData("Models", "x64", OemInfArchitectureEvidence.Verdict.Unknown)]
    [InlineData("Models.NT", "x64", OemInfArchitectureEvidence.Verdict.Unknown)]
    [InlineData("Models.NTamd64.10.0", "x64", OemInfArchitectureEvidence.Verdict.ArchitectureMatches)]
    public void ConservativelyReportsArchitectureOnly(string section, string arch,
        OemInfArchitectureEvidence.Verdict expected)
    {
        var result = OemInfArchitectureEvidence.Assess(Evidence(section), arch);
        var found = Assert.Single(result.Findings);
        Assert.Equal(expected, found.Result);
        Assert.False(result.IsInstallable);
        Assert.False(result.IsVerifiedCompatible);
    }

    [Fact]
    public void UnknownArchitectureNeverMatches()
    {
        Assert.Equal(OemInfArchitectureEvidence.Verdict.Unknown,
            Assert.Single(OemInfArchitectureEvidence.Assess(
                Evidence("Models.NTamd64"), "something").Findings).Result);
    }
}
