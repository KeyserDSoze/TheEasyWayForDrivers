using TheEasyWayForDrivers.Setup;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("TheEasyWayForDrivers setup supports Windows only.");
    return 1;
}

try
{
    var mode = args.Contains("--update", StringComparer.OrdinalIgnoreCase)
        ? SetupMode.Update
        : SetupMode.Install;

    var engine = new SetupEngine();
    await engine.ExecuteAsync(mode, CancellationToken.None);

    Console.WriteLine(
        mode == SetupMode.Update
            ? "TheEasyWayForDrivers updated successfully."
            : "TheEasyWayForDrivers installed successfully.");

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
