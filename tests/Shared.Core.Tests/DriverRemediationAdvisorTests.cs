using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverRemediationAdvisorTests
{
    private static DriverInfo Device(uint code = 28, bool hasDriver = false, bool hasIds = true) =>
        new("TEST", "Test", null, "System", null, null, null, null, false,
            hasDriver, code, hasIds ? [@"PCI\VEN_8086&DEV_1234"] : [], []);

    [Fact]
    public void BeforeSearch_RequestsWindowsUpdateScan() =>
        Assert.Contains("Esegui una ricerca", DriverRemediationAdvisor.GetNextAction(Device(), false, false, "Intel"));

    [Fact]
    public void WithMatch_RequiresExplicitReview() =>
        Assert.Contains("Verifica versione", DriverRemediationAdvisor.GetNextAction(Device(), true, true, "Windows Update"));

    [Fact]
    public void WithNoMatch_DoesNotClaimOfficialDriverExists() =>
        Assert.Contains("non è stata verificata", DriverRemediationAdvisor.GetNextAction(Device(), false, true, "Intel"));

    [Fact]
    public void WithoutHardwareIds_AdvisesModelIdentification() =>
        Assert.Contains("Nessun Hardware ID", DriverRemediationAdvisor.GetNextAction(Device(hasIds: false), false, true, "Windows / OEM"));

    [Fact]
    public void DisabledDevice_AdvisesDeviceManagerInsteadOfDownload() =>
        Assert.Contains("disabilitato", DriverRemediationAdvisor.GetNextAction(Device(code: 22, hasDriver: true), false, true, "Intel"));

    [Fact]
    public void UnverifiedDriver_DoesNotAdviseUnnecessaryInstallation()
    {
        var message = DriverRemediationAdvisor.GetNextAction(
            Device(code: 0, hasDriver: false), false, true, "Intel");
        Assert.Contains("Gestione dispositivi", message);
        Assert.Contains("non segnala errori", message);
    }

    [Fact]
    public void HealthyDevice_DoesNotSuggestUnnecessaryInstallation() =>
        Assert.Contains("Nessun problema", DriverRemediationAdvisor.GetNextAction(Device(code: 0, hasDriver: true), false, true, "Intel"));
}
