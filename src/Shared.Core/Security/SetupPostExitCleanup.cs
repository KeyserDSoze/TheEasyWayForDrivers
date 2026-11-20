using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Strict, numeric, cross-process request for a post-exit uninstall worker.
/// No caller-supplied deletion path is ever encoded in the arguments (#598).
/// </summary>
public readonly record struct SetupFinalizerRequest(
    int ParentProcessId, long ParentStartUtcTicks,
    InstalledRootIdentity OriginalIdentity);

public static class SetupPostExitCleanup
{
    public const string FinalizerCommand = "--finalize-uninstall";
    public const string FinalizerExecutableName =
        "OmegaDrive-UninstallFinalizer.exe";
    private const string PendingName = "cleanup-pending.txt";
    private const string SuccessName = "cleanup-success.txt";
    private const string FailureName = "cleanup-failure.txt";

    public static SetupFinalizerRequest ForCurrentProcess(
        InstalledRootIdentity originalIdentity)
    {
        using var process = Process.GetCurrentProcess();
        return new SetupFinalizerRequest(process.Id,
            process.StartTime.ToUniversalTime().Ticks, originalIdentity);
    }

    public static string[] BuildArguments(SetupFinalizerRequest request)
    {
        ValidateRequest(request);
        return [
            FinalizerCommand,
            request.ParentProcessId.ToString(CultureInfo.InvariantCulture),
            request.ParentStartUtcTicks.ToString(CultureInfo.InvariantCulture),
            request.OriginalIdentity.VolumeSerialNumber.ToString(
                CultureInfo.InvariantCulture),
            request.OriginalIdentity.FileIndexHigh.ToString(
                CultureInfo.InvariantCulture),
            request.OriginalIdentity.FileIndexLow.ToString(
                CultureInfo.InvariantCulture)
        ];
    }

    public static bool TryParseArguments(
        string[] arguments, out SetupFinalizerRequest request)
    {
        request = default;
        if (arguments is null || arguments.Length != 6 ||
            !string.Equals(arguments[0], FinalizerCommand,
                StringComparison.Ordinal) ||
            !int.TryParse(arguments[1], NumberStyles.None,
                CultureInfo.InvariantCulture, out var parentPid) ||
            !long.TryParse(arguments[2], NumberStyles.None,
                CultureInfo.InvariantCulture, out var startTicks) ||
            !uint.TryParse(arguments[3], NumberStyles.None,
                CultureInfo.InvariantCulture, out var volume) ||
            !uint.TryParse(arguments[4], NumberStyles.None,
                CultureInfo.InvariantCulture, out var high) ||
            !uint.TryParse(arguments[5], NumberStyles.None,
                CultureInfo.InvariantCulture, out var low) ||
            parentPid <= 0 || startTicks <= 0 ||
            (volume == 0 && high == 0 && low == 0))
            return false;

        request = new SetupFinalizerRequest(parentPid, startTicks,
            new InstalledRootIdentity(volume, high, low));
        return true;
    }

    private static void ValidateRequest(SetupFinalizerRequest request)
    {
        if (request.ParentProcessId <= 0 ||
            request.ParentStartUtcTicks <= 0 ||
            request.OriginalIdentity == default)
            throw new ArgumentException(
                "Richiesta di cleanup senza identità di processo/root.");
    }

    /// <summary>
    /// Only a direct OmegaDrive staging child under the current Temp may
    /// host this worker. Verify owner/protected ACL via a native root handle
    /// before the elevated process does anything destructive (#563/#598).
    /// </summary>
    public static string VerifyPrivateWorkerLocation(string workerPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(workerPath) ||
            !Path.IsPathFullyQualified(workerPath))
            throw new InvalidDataException(
                "Percorso del finalizer non valido.");

        var file = Path.GetFullPath(workerPath);
        var root = Path.GetDirectoryName(file) ??
            throw new InvalidDataException("Directory finalizer mancante.");
        var temp = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetFileName(file), FinalizerExecutableName,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(root), temp,
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(root).StartsWith(
                SetupStagingDirectory.Prefix, StringComparison.Ordinal))
            throw new InvalidDataException(
                "Il finalizer deve essere in una directory Temp privata.");

        using var anchor = SetupDirectoryAnchor.Open(root);
        SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor);
        return root;
    }

    /// <summary>
    /// The parent's PID alone is not identity: compare start time as well,
    /// then wait using that process handle. A recycled PID never blocks the
    /// worker waiting for an unrelated process (#598).
    /// </summary>
    public static bool IsOriginalParentRunning(SetupFinalizerRequest request)
    {
        ValidateRequest(request);
        using var parent = OpenOriginalParent(request);
        return parent is not null && !parent.HasExited;
    }

    public static void WaitForOriginalParentExit(SetupFinalizerRequest request)
    {
        ValidateRequest(request);
        using var parent = OpenOriginalParent(request);
        parent?.WaitForExit();
    }

    private static Process? OpenOriginalParent(SetupFinalizerRequest request)
    {
        Process parent;
        try
        {
            parent = Process.GetProcessById(request.ParentProcessId);
        }
        catch (ArgumentException)
        {
            // The original process has already exited.
            return null;
        }

        try
        {
            if (parent.StartTime.ToUniversalTime().Ticks !=
                request.ParentStartUtcTicks)
            {
                parent.Dispose();
                return null;
            }
            return parent;
        }
        catch
        {
            parent.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The returned status is observable in a private Temp directory.
    /// A marker is CREATE_NEW relative to an ACL-verified pinned directory,
    /// so a concurrent injected redirect cannot be followed by this write.
    /// Status files remain after the worker exits for diagnostics.
    /// </summary>
    public static string WriteStatus(
        string stagingDirectory, string phase, string message)
    {
        var filename = phase switch
        {
            "pending" => PendingName,
            "success" => SuccessName,
            "failure" => FailureName,
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
        VerifyPrivateWorkerLocation(
            Path.Combine(stagingDirectory, FinalizerExecutableName));
        using var anchor = SetupDirectoryAnchor.Open(stagingDirectory);
        SetupStagingDirectory.VerifyPrivateWindowsRoot(anchor);
        using var destination = WindowsHandleRelativeFile.CreateNew(
            anchor.LeafDirectoryHandle, filename);
        using (var writer = new StreamWriter(
                   destination, new UTF8Encoding(false), leaveOpen: true))
        {
            writer.WriteLine(DateTimeOffset.UtcNow.ToString(
                "O", CultureInfo.InvariantCulture));
            // Avoid embedding multi-line untrusted exception text in status.
            var clean = string.Concat(message.Select(c =>
                char.IsControl(c) ? ' ' : c));
            writer.WriteLine(clean.Length > 320 ? clean[..320] : clean);
        }
        return Path.Combine(stagingDirectory, filename);
    }

    /// <summary>
    /// Do not trust the original path after the parent's exit; only the
    /// recorded volume+file ID can authorize the native cleanup walker.
    /// </summary>
    public static InstallRootCleanupOutcome Finish(
        SetupFinalizerRequest request, string workerPath,
        string installRoot, string programFilesRoot)
    {
        var staging = VerifyPrivateWorkerLocation(workerPath);
        WaitForOriginalParentExit(request);
        var result = SetupInstalledRootCleanup.CleanupIfMatchingIdentity(
            installRoot, programFilesRoot, workerPath,
            request.OriginalIdentity);
        if (result != InstallRootCleanupOutcome.Removed)
            throw new InvalidDataException(
                "Il finalizer non ha completato la rimozione verificata.");
        WriteStatus(staging, "success",
            "Root OmegaDrive verificata e rimossa dopo la chiusura del Setup.");
        return result;
    }
}
