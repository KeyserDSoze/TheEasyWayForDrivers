using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TheEasyWayForDrivers.Core.Ipc;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

public static class ClientProcessVerifier
{
    public static ClientProcessInfo Inspect(SafePipeHandle pipeHandle)
    {
        if (!GetNamedPipeClientProcessId(pipeHandle, out var processId))
        {
            return new ClientProcessInfo(null, null, false);
        }

        string? executablePath = null;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            executablePath = process.MainModule?.FileName;
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        var programFiles =
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return new ClientProcessInfo(
            processId,
            executablePath,
            ClientExecutablePolicy.IsAuthorizedDesktopPath(
                executablePath,
                programFiles));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}

public sealed record ClientProcessInfo(
    uint? ProcessId,
    string? ExecutablePath,
    bool IsAuthorized);
