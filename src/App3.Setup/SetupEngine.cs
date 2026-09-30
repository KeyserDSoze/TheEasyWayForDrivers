using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace TheEasyWayForDrivers.Setup;

public enum SetupMode
{
    Install,
    Update,
    Uninstall
}

public sealed class SetupEngine
{
    private readonly IProgress<SetupProgress>? _progress;

    public SetupEngine(
        IProgress<SetupProgress>? progress = null)
    {
        _progress = progress;
    }

    private const string PayloadResourceName =
        "TheEasyWayForDrivers.Payload.zip";

    private const string ServiceName =
        "TheEasyWayForDrivers.Service";

    private const string StartupValueName =
        "TheEasyWayForDrivers";

    private const string UninstallRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TheEasyWayForDrivers";

    private readonly string _installRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "TheEasyWayForDrivers");

    private readonly string _programDataRoot = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData),
        "TheEasyWayForDrivers");

    private string RollbackRoot =>
        Path.Combine(_programDataRoot, "Rollback");

    public async Task ExecuteAsync(
        SetupMode mode,
        CancellationToken cancellationToken)
    {
        if (mode == SetupMode.Uninstall)
        {
            Uninstall();
            return;
        }

        Report(
            5,
            mode == SetupMode.Update
                ? "Preparazione aggiornamento..."
                : "Preparazione installazione...");

        var staging = Path.Combine(
            Path.GetTempPath(),
            "TheEasyWayForDrivers",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);
        var rollbackCreated = false;

        try
        {
            await ExtractPayloadAsync(staging, cancellationToken);

            Report(
                18,
                "Arresto dei componenti in esecuzione...");

            StopService();
            StopDesktop();

            if (mode == SetupMode.Update &&
                Directory.Exists(_installRoot))
            {
                Report(
                    28,
                    "Creazione snapshot di rollback...");

                CreateRollbackSnapshot();
                rollbackCreated = true;
            }

            Report(
                42,
                "Installazione dei file OmegaDrive...");

            ReplaceDirectory(
                Path.Combine(staging, "Service"),
                Path.Combine(_installRoot, "Service"));

            ReplaceDirectory(
                Path.Combine(staging, "Desktop"),
                Path.Combine(_installRoot, "Desktop"));

            CopyUpdater();

            Report(
                62,
                "Configurazione del servizio Windows...");

            ConfigureService();

            if (mode == SetupMode.Install)
            {
                Report(
                    72,
                    "Configurazione avvio con Windows...");

                ConfigureDesktopStartup();
            }
            else
            {
                Report(
                    72,
                    "Preferenza di avvio con Windows preservata.");
            }

            Report(
                82,
                "Registrazione di OmegaDrive in Windows...");

            RegisterUninstall(GetSetupVersion());

            Report(
                90,
                "Avvio del servizio OmegaDrive...");
            RunSc(
                throwOnError: true,
                "start",
                ServiceName);

            LaunchDesktopThroughExplorer();

            Report(
                100,
                mode == SetupMode.Update
                    ? "OmegaDrive aggiornato."
                    : "OmegaDrive installato.");
        }
        catch
        {
            if (mode == SetupMode.Update && rollbackCreated)
            {
                Report(
                    94,
                    "Aggiornamento non riuscito. Ripristino della versione precedente...");

                TryRestoreRollback();
            }

            throw;
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
            await source.CopyToAsync(
                destination,
                cancellationToken);
        }

        ZipFile.ExtractToDirectory(
            zipPath,
            staging,
            overwriteFiles: true);

        File.Delete(zipPath);
    }

    private void CreateRollbackSnapshot()
    {
        TryDeleteDirectory(RollbackRoot);
        Directory.CreateDirectory(RollbackRoot);

        CopyDirectoryIfExists(
            Path.Combine(_installRoot, "Service"),
            Path.Combine(RollbackRoot, "Service"));

        CopyDirectoryIfExists(
            Path.Combine(_installRoot, "Desktop"),
            Path.Combine(RollbackRoot, "Desktop"));

        CopyDirectoryIfExists(
            Path.Combine(_installRoot, "Updater"),
            Path.Combine(RollbackRoot, "Updater"));

        File.WriteAllText(
            Path.Combine(RollbackRoot, "version.txt"),
            GetInstalledVersion() ?? string.Empty);
    }

    private void TryRestoreRollback()
    {
        try
        {
            StopService();
            StopDesktop();

            RestoreDirectoryIfExists(
                Path.Combine(RollbackRoot, "Service"),
                Path.Combine(_installRoot, "Service"));

            RestoreDirectoryIfExists(
                Path.Combine(RollbackRoot, "Desktop"),
                Path.Combine(_installRoot, "Desktop"));

            CopyDirectoryIfExists(
                Path.Combine(RollbackRoot, "Updater"),
                Path.Combine(_installRoot, "Updater"));

            ConfigureService();

            var versionPath =
                Path.Combine(RollbackRoot, "version.txt");

            var rollbackVersion =
                File.Exists(versionPath)
                    ? File.ReadAllText(versionPath).Trim()
                    : null;

            RegisterUninstall(
                string.IsNullOrWhiteSpace(rollbackVersion)
                    ? "0.0.0"
                    : rollbackVersion);

            RunSc(
                throwOnError: false,
                "start",
                ServiceName);

            LaunchDesktopThroughExplorer();
        }
        catch (Exception rollbackException)
        {
            Console.Error.WriteLine(
                "Rollback also failed: " + rollbackException);
        }
    }

    private void Report(
        int percent,
        string message) =>
        _progress?.Report(
            new SetupProgress(
                percent,
                message));

    private void ConfigureService()
    {
        var serviceExe = Path.Combine(
            _installRoot,
            "Service",
            "App1.Service.exe");

        if (!File.Exists(serviceExe))
        {
            throw new FileNotFoundException(
                "Service executable was not found.",
                serviceExe);
        }

        var queryExitCode =
            RunSc(
                throwOnError: false,
                "query",
                ServiceName);

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
                "OmegaDrive Driver Service");
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
                "OmegaDrive Driver Service");
        }

        RunSc(
            throwOnError: false,
            "description",
            ServiceName,
            "OmegaDrive driver inventory, download and installation service.");

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
        var desktopExe = Path.Combine(
            _installRoot,
            "Desktop",
            "App2.Desktop.exe");

        using var runKey =
            Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true)
            ?? Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);

        runKey.SetValue(
            StartupValueName,
            $"\"{desktopExe}\"");
    }

    private void RegisterUninstall(string version)
    {
        var updaterExe = Path.Combine(
            _installRoot,
            "Updater",
            "OmegaDrive-Setup.exe");

        using var key =
            Registry.LocalMachine.CreateSubKey(
                UninstallRegistryPath,
                writable: true);

        key.SetValue(
            "DisplayName",
            "OmegaDrive Driver Manager");

        key.SetValue(
            "DisplayVersion",
            version);

        key.SetValue(
            "Publisher",
            "OmegaDrive");

        key.SetValue(
            "InstallLocation",
            _installRoot);

        key.SetValue(
            "DisplayIcon",
            $"\"{updaterExe}\"");

        key.SetValue(
            "UninstallString",
            $"\"{updaterExe}\" --uninstall");

        key.SetValue(
            "URLInfoAbout",
            "https://github.com/KeyserDSoze/TheEasyWayForDrivers");

        key.SetValue(
            "NoModify",
            1,
            RegistryValueKind.DWord);

        key.SetValue(
            "NoRepair",
            1,
            RegistryValueKind.DWord);
    }

    private void CopyUpdater()
    {
        var source = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(source) ||
            !File.Exists(source))
        {
            return;
        }

        var updaterDirectory =
            Path.Combine(_installRoot, "Updater");

        Directory.CreateDirectory(updaterDirectory);

        var destination = Path.Combine(
            updaterDirectory,
            "OmegaDrive-Setup.exe");

        var legacyDestination =
            Path.Combine(
                updaterDirectory,
                "TheEasyWayForDrivers-Setup.exe");

        if (File.Exists(legacyDestination))
        {
            try
            {
                File.Delete(legacyDestination);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (string.Equals(
            Path.GetFullPath(source),
            Path.GetFullPath(destination),
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Copy(
            source,
            destination,
            overwrite: true);
    }

    private void Uninstall()
    {
        Report(
            10,
            "Arresto dei componenti OmegaDrive...");

        StopService();
        StopDesktop();

        Report(
            35,
            "Rimozione del servizio Windows...");

        RunSc(
            throwOnError: false,
            "delete",
            ServiceName);

        Report(
            55,
            "Rimozione delle voci di avvio...");

        RemoveDesktopStartup();

        Report(
            70,
            "Rimozione della registrazione applicazione...");

        Registry.LocalMachine.DeleteSubKeyTree(
            UninstallRegistryPath,
            throwOnMissingSubKey: false);

        TryDeleteDirectory(
            Path.Combine(_installRoot, "Service"));

        TryDeleteDirectory(
            Path.Combine(_installRoot, "Desktop"));

        TryDeleteDirectory(_programDataRoot);

        ScheduleInstallRootDeletion();

        Report(
            100,
            "OmegaDrive è stato rimosso.");
    }

    private static void StopDesktop()
    {
        foreach (var process in
                 Process.GetProcessesByName("App2.Desktop"))
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
        RunSc(
            throwOnError: false,
            "stop",
            ServiceName);

        Thread.Sleep(1500);
    }

    private void RemoveDesktopStartup()
    {
        DeleteStartupValue(Registry.CurrentUser);

        foreach (var userSid in Registry.Users.GetSubKeyNames())
        {
            try
            {
                using var userRoot =
                    Registry.Users.OpenSubKey(
                        userSid,
                        writable: true);

                if (userRoot is not null)
                {
                    DeleteStartupValue(userRoot);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void DeleteStartupValue(
        RegistryKey root)
    {
        try
        {
            using var runKey = root.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);

            runKey?.DeleteValue(
                StartupValueName,
                throwOnMissingValue: false);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void LaunchDesktopThroughExplorer()
    {
        var desktopExe = Path.Combine(
            _installRoot,
            "Desktop",
            "App2.Desktop.exe");

        if (!File.Exists(desktopExe))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{desktopExe}\"",
            UseShellExecute = true
        });
    }

    private void ScheduleInstallRootDeletion()
    {
        if (!Directory.Exists(_installRoot))
        {
            return;
        }

        var command =
            $"ping 127.0.0.1 -n 3 > nul & " +
            $"rmdir /s /q \"{_installRoot}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/d /c \"{command}\""
        });
    }

    private static int RunSc(
        bool throwOnError,
        params string[] arguments)
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

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Unable to start sc.exe.");

        process.WaitForExit();

        var output =
            process.StandardOutput.ReadToEnd();

        var error =
            process.StandardError.ReadToEnd();

        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"sc.exe failed with exit code {process.ExitCode}. " +
                $"{output} {error}".Trim());
        }

        return process.ExitCode;
    }

    private static void ReplaceDirectory(
        string source,
        string destination)
    {
        TryDeleteDirectory(destination);
        CopyDirectory(source, destination);
    }

    private static void RestoreDirectoryIfExists(
        string source,
        string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        ReplaceDirectory(source, destination);
    }

    private static void CopyDirectoryIfExists(
        string source,
        string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        CopyDirectory(source, destination);
    }

    private static void CopyDirectory(
        string source,
        string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"Payload directory not found: {source}");
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(
                    destination,
                    Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (var directory in
                 Directory.EnumerateDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(
                    destination,
                    Path.GetFileName(directory)));
        }
    }

    private string? GetInstalledVersion()
    {
        var desktopExe = Path.Combine(
            _installRoot,
            "Desktop",
            "App2.Desktop.exe");

        if (!File.Exists(desktopExe))
        {
            return null;
        }

        var version =
            FileVersionInfo
                .GetVersionInfo(desktopExe)
                .FileVersion;

        return string.IsNullOrWhiteSpace(version)
            ? null
            : version;
    }

    private static string GetSetupVersion()
    {
        var version =
            Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version
            ?? new Version(0, 0, 1);

        return
            $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
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
