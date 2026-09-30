using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace TheEasyWayForDrivers.Setup;

public enum SetupMode
{
    Install,
    Update
}

public sealed class SetupEngine
{
    private const string PayloadResourceName = "TheEasyWayForDrivers.Payload.zip";
    private const string ServiceName = "TheEasyWayForDrivers.Service";
    private const string StartupValueName = "TheEasyWayForDrivers";

    private readonly string _installRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "TheEasyWayForDrivers");

    public async Task ExecuteAsync(SetupMode mode, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{mode}: preparing payload...");

        var staging = Path.Combine(
            Path.GetTempPath(),
            "TheEasyWayForDrivers",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);

        try
        {
            await ExtractPayloadAsync(staging, cancellationToken);

            Console.WriteLine("Stopping running components...");
            StopService();
            StopDesktop();

            Console.WriteLine("Installing application files...");
            CopyDirectory(Path.Combine(staging, "Service"), Path.Combine(_installRoot, "Service"));
            CopyDirectory(Path.Combine(staging, "Desktop"), Path.Combine(_installRoot, "Desktop"));
            CopyUpdater();

            Console.WriteLine("Configuring Windows service...");
            ConfigureService();

            Console.WriteLine("Configuring tray application startup...");
            ConfigureDesktopStartup();

            Console.WriteLine("Starting Windows service...");
            RunSc(throwOnError: true, "start", ServiceName);

            LaunchDesktopThroughExplorer();
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static async Task ExtractPayloadAsync(
        string staging,
        CancellationToken cancellationToken)
    {
        await using var source = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                "The embedded application payload is missing. " +
                "Use the setup executable produced by the GitHub release workflow.");

        var zipPath = Path.Combine(staging, "payload.zip");
        await using (var destination = File.Create(zipPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
        File.Delete(zipPath);
    }

    private void ConfigureService()
    {
        var serviceExe = Path.Combine(_installRoot, "Service", "App1.Service.exe");
        if (!File.Exists(serviceExe))
        {
            throw new FileNotFoundException("Service executable was not found.", serviceExe);
        }

        var queryExitCode = RunSc(throwOnError: false, "query", ServiceName);

        if (queryExitCode == 0)
        {
            RunSc(
                throwOnError: true,
                "config",
                ServiceName,
                "binPath=",
                serviceExe,
                "start=",
                "auto",
                "DisplayName=",
                "TheEasyWayForDrivers Service");
        }
        else
        {
            RunSc(
                throwOnError: true,
                "create",
                ServiceName,
                "binPath=",
                serviceExe,
                "start=",
                "auto",
                "DisplayName=",
                "TheEasyWayForDrivers Service");
        }

        RunSc(
            throwOnError: false,
            "description",
            ServiceName,
            "Driver inventory, download and installation service.");

        RunSc(
            throwOnError: false,
            "failure",
            ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/5000");
    }

    private void ConfigureDesktopStartup()
    {
        var desktopExe = Path.Combine(_installRoot, "Desktop", "App2.Desktop.exe");

        using var runKey = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true)
            ?? Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);

        runKey.SetValue(StartupValueName, $""{desktopExe}"");
    }

    private void CopyUpdater()
    {
        var source = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return;
        }

        var updaterDirectory = Path.Combine(_installRoot, "Updater");
        Directory.CreateDirectory(updaterDirectory);

        var destination = Path.Combine(
            updaterDirectory,
            "TheEasyWayForDrivers-Setup.exe");

        if (string.Equals(
            Path.GetFullPath(source),
            Path.GetFullPath(destination),
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static void StopDesktop()
    {
        foreach (var process in Process.GetProcessesByName("App2.Desktop"))
        {
            using (process)
            {
                try
                {
                    process.CloseMainWindow();

                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private static void StopService()
    {
        RunSc(throwOnError: false, "stop", ServiceName);
        Thread.Sleep(1500);
    }

    private void LaunchDesktopThroughExplorer()
    {
        var desktopExe = Path.Combine(_installRoot, "Desktop", "App2.Desktop.exe");

        if (!File.Exists(desktopExe))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $""{desktopExe}"",
            UseShellExecute = true
        });
    }

    private static int RunSc(bool throwOnError, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start sc.exe.");

        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sc.exe failed with exit code {process.ExitCode}. {output} {error}".Trim());
        }

        return process.ExitCode;
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Payload directory not found: {source}");
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(destination, Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
