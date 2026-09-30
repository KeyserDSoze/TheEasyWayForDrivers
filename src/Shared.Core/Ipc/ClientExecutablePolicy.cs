namespace TheEasyWayForDrivers.Core.Ipc;

public static class ClientExecutablePolicy
{
    public static bool IsAuthorizedDesktopPath(
        string? executablePath,
        string programFilesPath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            string.IsNullOrWhiteSpace(programFilesPath))
        {
            return false;
        }

        try
        {
            var expected = Path.GetFullPath(Path.Combine(
                programFilesPath,
                "TheEasyWayForDrivers",
                "Desktop",
                "App2.Desktop.exe"));

            var actual = Path.GetFullPath(executablePath);

            return string.Equals(
                expected,
                actual,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
