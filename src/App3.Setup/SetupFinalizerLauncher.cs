using System.Diagnostics;
using TheEasyWayForDrivers.Core.Security;

namespace TheEasyWayForDrivers.Setup;

/// <summary>
/// Copy the currently-running trusted setup into a private Temp directory
/// and launch an elevated-token child before the parent exits (#598).
/// No shell interpreter, installer download, or user-selected delete path.
/// </summary>
internal static class SetupFinalizerLauncher
{
    public static string Start(string installRoot, string processPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        if (!Path.IsPathFullyQualified(processPath))
            throw new InvalidDataException(
                "Il Setup in esecuzione non ha un percorso assoluto.");

        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var identity = SetupInstalledRootCleanup.CaptureWindowsIdentity(
            installRoot, programFiles);
        var request = SetupPostExitCleanup.ForCurrentProcess(identity);
        var staging = SetupStagingDirectory.Create();
        var workerPath = Path.Combine(staging,
            SetupPostExitCleanup.FinalizerExecutableName);
        try
        {
            SetupTreeCopyGuard.CopySingleFile(
                processPath, workerPath, overwrite: false);
            SetupPostExitCleanup.VerifyPrivateWorkerLocation(workerPath);
            SetupPostExitCleanup.WriteStatus(staging, "pending",
                "Pulizia automatica in attesa della chiusura del Setup.");
            var startInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = staging
            };
            foreach (var arg in SetupPostExitCleanup.BuildArguments(request))
                startInfo.ArgumentList.Add(arg);

            // Inherit the current elevated token; do not use runas,
            // an unverified detached shell command, or a new UAC request.
            using var child = Process.Start(startInfo) ??
                throw new IOException(
                    "Impossibile avviare il finalizer di disinstallazione.");
            return staging;
        }
        catch
        {
            // The worker was not successfully scheduled, and is not in use:
            // clean only the newly-created protected staging directory.
            SetupStagingDirectory.DeleteIfExists(staging);
            throw;
        }
    }
}
