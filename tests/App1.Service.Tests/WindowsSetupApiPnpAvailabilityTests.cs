using System.Runtime.InteropServices;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// Native ABI availability on the actual Windows CI runner. Real notebook
/// applicability still requires QA on Recoil 16; no device is modified.
/// </summary>
public sealed class WindowsSetupApiPnpAvailabilityTests
{
    [Fact]
    public void OfficialSetupApiDeviceIdentityReadProceduresExistOnWindowsRunner()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var setupApi = NativeLibrary.Load("setupapi.dll");
        try
        {
            foreach (var function in new[]
            {
                "SetupDiGetClassDevsW",
                "SetupDiOpenDeviceInfoW",
                "SetupDiGetDeviceRegistryPropertyW",
                "SetupDiDestroyDeviceInfoList"
            })
                Assert.True(NativeLibrary.TryGetExport(setupApi, function, out _),
                    "SetupAPI identity reading unavailable: " + function);
        }
        finally
        {
            NativeLibrary.Free(setupApi);
        }
    }
}
