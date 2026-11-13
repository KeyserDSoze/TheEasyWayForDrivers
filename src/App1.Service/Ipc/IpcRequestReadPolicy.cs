using System.IO;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

/// <summary>
/// Bounds the time to receive a request, not the subsequent inventory,
/// Windows Update discovery, driver installation or diagnostics operation.
/// </summary>
public static class IpcRequestReadPolicy
{
    public static async Task<BoundedIpcRequestReader.ReadResult> ReadAsync(
        TextReader reader,
        int maximumCharacters,
        TimeSpan readTimeout,
        CancellationToken serviceCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (readTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(readTimeout));

        serviceCancellationToken.ThrowIfCancellationRequested();
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(serviceCancellationToken);
        linked.CancelAfter(readTimeout);
        try
        {
            return await BoundedIpcRequestReader.ReadLineAsync(
                reader, maximumCharacters, linked.Token);
        }
        catch (OperationCanceledException) when (
            !serviceCancellationToken.IsCancellationRequested &&
            linked.IsCancellationRequested)
        {
            throw new TimeoutException("IPC request read timed out.");
        }
    }
}
