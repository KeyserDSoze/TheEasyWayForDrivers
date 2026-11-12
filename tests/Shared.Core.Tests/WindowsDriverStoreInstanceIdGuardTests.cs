using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #408: do not pass arbitrary legacy IPC strings or a hardware ID
/// as the instance-ID argument to either PnPUtil or native SetupAPI.
/// </summary>
public sealed class WindowsDriverStoreInstanceIdGuardTests
{
    private static DriverInfo Device(string id) =>
        new(id, "Windows device", "Microsoft", "System", "Microsoft",
            "1.0", null, null, true, true, 0, [], []);

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\4&12345ABC&0&0008")]
    [InlineData(@"PCI\VEN_8086&DEV_51F1&SUBSYS_16721A56\3&ABC&0&FA")]
    [InlineData(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009B\5&1234&0&0001")]
    [InlineData(@"USB\VID_0BDA&PID_5655\SERIAL NUMBER WITH SPACES")]
    [InlineData(@"USBSTOR\DISK&VEN_SANDISK&PROD_CRUZER\3&123&0")]
    [InlineData(@"ACPI\NVDA0820\2")]
    [InlineData(@"SWD\MMDEVAPI\{00000000-0000-4000-8000-1234567890AB}")]
    [InlineData(@"ROOT\NET\0000")]
    [InlineData(@"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&00010000\8&1234&0&BLUETOOTHDEVICE")]
    [InlineData(@"HID\VID_046D&PID_C52B\8&PRIVATE&0&0000")]
    [InlineData(@"DISPLAY\LGD06ED\5&12AB&0&UID265")]
    [InlineData(@"HTREE\ROOT\0")]
    public void WellFormedWindowsInstanceIdsRemainReadOnly(string id)
    {
        var args = WindowsDriverStoreEvidence.CommandArguments(Device(id));
        Assert.Equal(["/enum-devices", "/instanceid", id, "/drivers"], args);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("/delete-driver")]
    [InlineData("/enum-devices")]
    [InlineData("NVIDIA")]
    [InlineData("PCI")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\")]
    [InlineData(@"PCI\\4&1234")]
    [InlineData(@"\\SERVER\share\PRIVATE")]
    [InlineData(@"C:\Windows\System32\drivers")]
    [InlineData("https://private.example/devices")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\..\DEVICE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\.\DEVICE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\4&1234\..\PRIVATE")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\4/1234")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\4:1234")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\ --help")]
    [InlineData(@"PCI\VEN_10DE&DEV_28A0\ -install")]
    [InlineData(@" PCI\VEN_10DE&DEV_28A0\4")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4 ")]
    [InlineData(@"bad path\VEN_10DE&DEV_28A0\4")]
    public void RejectsNonInstanceAndSwitchOrPathShapedInput(string badId)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(Device(badId)));
        Assert.DoesNotContain(badId, error.Message);
        Assert.Contains("non utilizzabile", error.Message);
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4\n/delete-driver")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4\u202E")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4\"")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4'")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4\u2066")]
    [InlineData("PCI\\VEN_10DE&DEV_28A0\\4\0")]
    public void ControlAndBidiCannotReachNativeOrCommandLine(string badId) =>
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(Device(badId)));

    [Fact]
    public void DeviceInstanceAndHardwareIdAreNotEquivalent()
    {
        const string hardware = @"PCI\VEN_10DE&DEV_28A0";
        const string instance = @"PCI\VEN_10DE&DEV_28A0\4";
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(Device(hardware)));
        Assert.Equal(instance,
            WindowsDriverStoreEvidence.CommandArguments(Device(instance))[2]);
    }

    [Fact]
    public void OverlongOrEmptyInputsAreStillRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(Device("")));
        Assert.Throws<InvalidDataException>(() =>
            WindowsDriverStoreEvidence.CommandArguments(
                Device(@"PCI\VEN_8086&DEV_1234\" + new string('X', 512))));
    }
}
