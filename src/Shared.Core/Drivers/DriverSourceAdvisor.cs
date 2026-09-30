using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class DriverSourceAdvisor
{
    public static string GetSource(
        DriverInfo driver,
        IReadOnlyCollection<DriverUpdateInfo> matchedUpdates)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(matchedUpdates);

        if (matchedUpdates.Count > 0)
        {
            return "Windows Update";
        }

        if (NvidiaHardwareDetector.IsNvidiaDevice(driver))
        {
            return "NVIDIA";
        }

        if (AmdHardwareDetector.IsAmdDevice(driver))
        {
            return "AMD";
        }

        if (IntelHardwareDetector.IsIntelDevice(driver))
        {
            return "Intel";
        }

        return "Windows / OEM";
    }
}
