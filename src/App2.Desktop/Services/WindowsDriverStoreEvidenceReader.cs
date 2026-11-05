using System.Diagnostics;
using System.IO;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Runs only Windows' documented read-only PnPUtil per-device enumeration.
/// Output stays in the local WPF view, is bounded, and never goes into
/// clipboard diagnostics, telemetry, logs or Windows Update installation.
/// </summary>
public static class WindowsDriverStoreEvidenceReader
{
    private const int TimeoutSeconds = 25;

    public static async Task<WindowsDriverStoreEvidence.Report> ReadAsync(
        DriverInfo device, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PnPUtil richiede Windows.");

        var arguments = WindowsDriverStoreEvidence.CommandArguments(device);
        var tool = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        if (!File.Exists(tool) ||
            (File.GetAttributes(tool) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("PnPUtil di Windows non disponibile.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.Default,
            StandardErrorEncoding = Encoding.Default
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ??
            throw new InvalidOperationException("Impossibile avviare PnPUtil.");
        try
        {
            var outputTask = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errorTask = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(
                outputTask, errorTask, process.WaitForExitAsync(timeout.Token));
            // Do not expose raw stderr: it can contain device instance IDs or
            // locally identifying file paths.
            return WindowsDriverStoreEvidence.FromCommandResult(
                process.ExitCode, outputTask.Result, device);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Lettura PnPUtil oltre il limite di tempo.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader input, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (output.Length + count > WindowsDriverStoreEvidence.MaxOutputCharacters)
                throw new InvalidDataException("PnPUtil ha prodotto troppi dati.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}
