using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// Bounded stdout/stderr capture for opt-in HP, Dell and Lenovo companion queries.
/// Never log captured text (it may contain device identifiers or paths).
/// A misbehaving module must not exhaust the Desktop's memory.
/// </summary>
internal static class OemCompanionOutputReader
{
    public static async Task<string> ReadBoundedAsync(
        Process process, StreamReader reader, int maximumCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));

        var output = new StringBuilder(Math.Min(maximumCharacters, 8192));
        var buffer = new char[8192];
        while (true)
        {
            var size = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (size == 0)
                return output.ToString();
            if (output.Length > maximumCharacters - size)
            {
                // Kill BEFORE the caller awaits process exit: otherwise a
                // blocked child could keep the other redirected pipe open.
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                throw new InvalidDataException(
                    "Output del modulo OEM oltre i limiti di sicurezza.");
            }
            output.Append(buffer, 0, size);
        }
    }
}
