using System.ComponentModel;
using System.Runtime.InteropServices;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Reads local Windows SetupAPI-compatible driver nodes and their actual
/// rank on demand; no device mutation, staging or third-party INF import.
/// </summary>
public static class WindowsSetupApiRankReader
{
    private const uint PresentAllClasses = 0x00000002 | 0x00000004;
    private const uint CompatibleDrivers = 2;
    private const int NoMoreItems = 259;

    public static Task<WindowsSetupApiRankEvidence.Report> ReadAsync(
        DriverInfo device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("SetupAPI richiede Windows.");

        // Shared validation prevents malformed or injected instance IDs.
        _ = WindowsDriverStoreEvidence.CommandArguments(device);
        return Task.Run(() => Read(device.DeviceId, cancellationToken),
            cancellationToken);
    }

    private static WindowsSetupApiRankEvidence.Report Read(
        string instanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero,
            PresentAllClasses);
        if (set == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        try
        {
            var device = new SpDevinfoData
            {
                CbSize = (uint)Marshal.SizeOf<SpDevinfoData>()
            };
            if (!SetupDiOpenDeviceInfo(set, instanceId, IntPtr.Zero, 0,
                ref device))
                return WindowsSetupApiRankEvidence.NotVerified();

            cancellationToken.ThrowIfCancellationRequested();
            if (!SetupDiBuildDriverInfoList(set, ref device, CompatibleDrivers))
                return WindowsSetupApiRankEvidence.NotVerified();

            try
            {
                var results = new List<WindowsSetupApiRankEvidence.Driver>();
                bool complete = true;
                for (uint index = 0;
                     index < WindowsSetupApiRankEvidence.MaxEnumeratedDrivers;
                     index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new SpDrvinfoData
                    {
                        CbSize = (uint)Marshal.SizeOf<SpDrvinfoData>()
                    };
                    if (!SetupDiEnumDriverInfo(set, ref device, CompatibleDrivers,
                        index, ref info))
                    {
                        if (Marshal.GetLastPInvokeError() != NoMoreItems)
                            complete = false;
                        return WindowsSetupApiRankEvidence.Summarize(
                            results, complete);
                    }

                    var install = new SpDrvinstallParams
                    {
                        CbSize = (uint)Marshal.SizeOf<SpDrvinstallParams>()
                    };
                    if (!SetupDiGetDriverInstallParams(set, ref device,
                        ref info, ref install))
                    {
                        // Missing rank is unknown, never zero (best).
                        complete = false;
                        continue;
                    }

                    results.Add(new WindowsSetupApiRankEvidence.Driver(
                        install.Rank, info.Description ?? "",
                        info.ProviderName ?? "", info.DriverVersion,
                        WindowsSetupApiRankEvidence.FromNativeFileTime(
                            info.DriverDateFileTime)));
                }

                // Reaching the native read cap means coverage is partial.
                return WindowsSetupApiRankEvidence.Summarize(results, false);
            }
            finally
            {
                SetupDiDestroyDriverInfoList(set, ref device, CompatibleDrivers);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint CbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SpDrvinfoData
    {
        public uint CbSize;
        public uint DriverType;
        public IntPtr Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string MfgName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProviderName;
        public long DriverDateFileTime;
        public ulong DriverVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDrvinstallParams
    {
        public uint CbSize;
        public uint Rank;
        public uint Flags;
        public IntPtr PrivateData;
        public uint Reserved;
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

    [DllImport("setupapi.dll", EntryPoint = "SetupDiBuildDriverInfoList",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiBuildDriverInfoList(
        IntPtr set, ref SpDevinfoData device, uint type);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiEnumDriverInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDriverInfo(
        IntPtr set, ref SpDevinfoData device, uint type, uint index,
        ref SpDrvinfoData info);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDriverInstallParamsW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDriverInstallParams(
        IntPtr set, ref SpDevinfoData device, ref SpDrvinfoData info,
        ref SpDrvinstallParams install);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiDestroyDriverInfoList",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDriverInfoList(
        IntPtr set, ref SpDevinfoData device, uint type);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiDestroyDeviceInfoList",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
