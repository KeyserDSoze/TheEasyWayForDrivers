using TheEasyWayForDrivers.Setup;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine(
        "TheEasyWayForDrivers setup supports Windows only.");
    return 1;
}

try
{
    var mode =
        args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase)
            ? SetupMode.Uninstall
            : args.Contains("--update", StringComparer.OrdinalIgnoreCase)
                ? SetupMode.Update
                : SetupMode.Install;

    var engine = new SetupEngine();
    await engine.ExecuteAsync(mode, CancellationToken.None);

    Console.WriteLine(mode switch
    {
        SetupMode.Update =>
            "TheEasyWayForDrivers updated successfully.",
        SetupMode.Uninstall =>
            "TheEasyWayForDrivers uninstalled successfully.",
        _ =>
            "TheEasyWayForDrivers installed successfully."
    });

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
