using System.Diagnostics;
using System.Reflection;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Infrastructure;

public sealed class ServiceDiagnosticsReader
{
    private const int MaximumLogLines = 200;

    public ServiceDiagnosticsInfo Read()
    {
        var process = Process.GetCurrentProcess();
        var currentLogFile = GetCurrentLogFile();

        return new ServiceDiagnosticsInfo(
            FormatVersion(
                Assembly.GetExecutingAssembly().GetName().Version ??
                new Version(0, 0, 1)),
            new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Environment.ProcessId,
            ServicePaths.LogDirectory,
            currentLogFile,
            ReadTail(currentLogFile, MaximumLogLines));
    }

    private static string? GetCurrentLogFile()
    {
        try
        {
            if (!Directory.Exists(ServicePaths.LogDirectory))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(ServicePaths.LogDirectory, "service-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadTail(string? path, int maximumLines)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(stream);
            var lines = new Queue<string>(maximumLines);

            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == maximumLines)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }

            return lines.ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
