using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TheEasyWayForDrivers.Core.Ipc;
using TheEasyWayForDrivers.ServiceApp.Ipc;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// #551: run the genuine Windows ACL-protected service pipe, rather than
/// relying only on mocked dispatcher calls. This validates ONLY the negative
/// authorization path; an installed Desktop/Service E2E remains #85.
/// </summary>
public sealed class NamedPipeServerWindowsTransportTests
{
    [Fact]
    public async Task ActualWindowsPipeRejectsUninstalledTesthostTwiceBeforeDispatch()
    {
        // The test process is not the installed OmegaDrive Desktop binary.
        // If this precondition changes on the runner, do not accidentally
        // exercise the real dispatcher (intentionally absent here).
        Assert.True(OperatingSystem.IsWindows());
        Assert.False(ClientExecutablePolicy.IsAuthorizedDesktopPath(
            Environment.ProcessPath,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));

        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        // A null dispatcher is safe ONLY because both client connections
        // must be denied by the production ClientProcessVerifier first.
        // A regression that allows either client causes this test to fail.
        var server = new NamedPipeServer(
            null!, NullLogger<NamedPipeServer>.Instance);
        var serverTask = server.RunAsync(cancellation.Token);

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var client = new NamedPipeClientStream(
                    ".", NamedPipeServer.PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(8000, cancellation.Token);
                using var reader = new StreamReader(
                    client, Encoding.UTF8, leaveOpen: true);

                // Never write a command: the verifier must reject the
                // uninstalled process before attempting to read JSON.
                var frame = await reader.ReadLineAsync(cancellation.Token);
                Assert.NotNull(frame);
                using var response = JsonDocument.Parse(frame);
                var root = response.RootElement;
                Assert.Equal("error",
                    root.GetProperty("kind").GetString());
                Assert.False(root.GetProperty("success").GetBoolean());
                Assert.Equal(JsonValueKind.Null,
                    root.GetProperty("data").ValueKind);
                Assert.Equal(
                    "IPC client is not the installed TheEasyWayForDrivers desktop application.",
                    root.GetProperty("errorMessage").GetString());
                Assert.DoesNotContain("PRIVATE_", frame);
            }
        }
        finally
        {
            cancellation.Cancel();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(8));
        }
    }
}
