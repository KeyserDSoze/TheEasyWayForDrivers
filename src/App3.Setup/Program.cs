using System.Windows.Forms;
using TheEasyWayForDrivers.Setup;

if (!OperatingSystem.IsWindows())
{
    return 1;
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
