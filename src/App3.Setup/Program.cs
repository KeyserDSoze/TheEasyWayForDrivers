using System.Windows.Forms;
using TheEasyWayForDrivers.Core.Security;
using TheEasyWayForDrivers.Setup;

if (!OperatingSystem.IsWindows())
{
    return 1;
}

// Internal, non-interactive finalizer launched only from protected Temp.
// Reject any malformed arguments or worker placed outside private staging.
if (args.Length > 0 && string.Equals(args[0],
        SetupPostExitCleanup.FinalizerCommand, StringComparison.Ordinal))
{
    if (!SetupPostExitCleanup.TryParseArguments(args, out var request))
        return 1;

    var worker = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(worker))
        return 1;

    string? staging = null;
    try
    {
        staging = SetupPostExitCleanup.VerifyPrivateWorkerLocation(worker);
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var installRoot = Path.Combine(programFiles, "TheEasyWayForDrivers");
        SetupPostExitCleanup.Finish(
            request, worker, installRoot, programFiles);
        return 0;
    }
    catch (Exception error)
    {
        if (staging is not null)
        {
            try
            {
                SetupPostExitCleanup.WriteStatus(
                    staging, "failure",
                    error.GetType().Name + ": " + error.Message);
            }
            catch
            {
                // A failed audit write does not justify touching a different
                // target. The finalizer still exits nonzero, fail closed.
            }
        }
        return 1;
    }
}

var mode =
    args.Contains(
        "--uninstall",
        StringComparer.OrdinalIgnoreCase)
        ? SetupMode.Uninstall
        : args.Contains(
            "--update",
            StringComparer.OrdinalIgnoreCase)
            ? SetupMode.Update
            : SetupMode.Install;

ApplicationConfiguration.Initialize();

using var form =
    new SetupForm(mode);

Application.Run(form);

return form.ExitCode;
