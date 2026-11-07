using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Optional native Windows PnP identity read. A single PRESENT/ALLCLASSES
/// SetupAPI device set is reused throughout one WMI inventory scan.
/// Opens each item by its exact WMI DeviceID, never by friendly name.
/// No SetupDiSet*, device installation, driver staging or registry writes.
/// </summary>
internal sealed class WindowsSetupApiPnpIdentityReader : IDisposable
{
    private const uint PresentAllClasses = 0x00000002 | 0x00000004;
    private const uint HardwareId = 0x00000001;    // SPDRP_HARDWAREID
    private const uint CompatibleId = 0x00000002;  // SPDRP_COMPATIBLEIDS
    private const int InsufficientBuffer = 122;
    private const int InitialBytes = 4096;
    private readonly IntPtr _deviceSet;
    private bool _disposed;

    private WindowsSetupApiPnpIdentityReader(IntPtr set) => _deviceSet = set;

    public sealed record Identity(
        IReadOnlyList<string> HardwareIds,
        IReadOnlyList<string> CompatibleIds)
    {
        public static Identity Empty { get; } = new([], []);
    }

    public static WindowsSetupApiPnpIdentityReader? TryOpen()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            var set = SetupDiGetClassDevs(
                IntPtr.Zero, null, IntPtr.Zero, PresentAllClasses);
            return set == new IntPtr(-1) || set == IntPtr.Zero
                ? null : new WindowsSetupApiPnpIdentityReader(set);
        }
        catch (Exception error) when (
            error is not OperationCanceledException &&
            error is not OutOfMemoryException)
        {
            // SetupAPI is a BEST-EFFORT read-only backup. Native API
            // unavailable must not erase the existing WMI PnP inventory.
            return null;
        }
    }

    public Identity ReadMissing(
        string deviceInstanceId, bool needHardware, bool needCompatible,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || (!needHardware && !needCompatible) ||
            string.IsNullOrWhiteSpace(deviceInstanceId) ||
            deviceInstanceId.Length > 512 ||
            deviceInstanceId.Any(ch => char.IsControl(ch) ||
                ch is '"' or '\'' or '\u202E'))
            return Identity.Empty;

        try
        {
            var dev = new SpDevinfoData
            {
                CbSize = (uint)Marshal.SizeOf<SpDevinfoData>()
            };
            if (!SetupDiOpenDeviceInfo(
                _deviceSet, deviceInstanceId, IntPtr.Zero, 0, ref dev))
                return Identity.Empty;

            var hardware = needHardware
                ? ReadIds(ref dev, HardwareId) : [];
            cancellationToken.ThrowIfCancellationRequested();
            var compatible = needCompatible
                ? ReadIds(ref dev, CompatibleId) : [];
            return new Identity(hardware, compatible);
        }
        catch (Exception error) when (
            error is not OperationCanceledException &&
            error is not OutOfMemoryException)
        {
            return Identity.Empty;
        }
    }

    private IReadOnlyList<string> ReadIds(ref SpDevinfoData dev, uint property)
    {
        var buffer = new byte[InitialBytes];
        if (!SetupDiGetDeviceRegistryProperty(
            _deviceSet, ref dev, property, out var type,
            buffer, (uint)buffer.Length, out var required))
        {
            if (Marshal.GetLastPInvokeError() != InsufficientBuffer ||
                required is < 4 or > PnpSetupApiIdentity.MaximumBytes)
                return [];

            buffer = new byte[(int)required];
            if (!SetupDiGetDeviceRegistryProperty(
                _deviceSet, ref dev, property, out type,
                buffer, (uint)buffer.Length, out required))
                return [];
        }

        return PnpSetupApiIdentity.Decode(buffer, type, required);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SetupDiDestroyDeviceInfoList(_deviceSet);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInfo(
        IntPtr set, string instanceId, IntPtr parent, uint flags,
        ref SpDevinfoData device);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr set, ref SpDevinfoData device, uint property,
        out uint type, [Out] byte[] buffer, uint bufferBytes,
        out uint requiredBytes);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiDestroyDeviceInfoList",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
