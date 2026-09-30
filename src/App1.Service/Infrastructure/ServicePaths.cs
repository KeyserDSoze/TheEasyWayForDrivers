namespace TheEasyWayForDrivers.ServiceApp.Infrastructure;

public static class ServicePaths
{
    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "TheEasyWayForDrivers",
            "Logs");
}
