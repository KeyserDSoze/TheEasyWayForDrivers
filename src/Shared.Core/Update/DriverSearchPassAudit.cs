using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Runs one independent read-only catalog query. Never reports failure as zero matches.
/// Cancellation requested by the caller is never swallowed.
/// </summary>
public static class DriverSearchPassAudit
{
    public static DriverSearchPassStatus Run(
        string source,
        Func<int> query,
        CancellationToken cancellationToken,
        Action<Exception>? onFailure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var count = query();
            cancellationToken.ThrowIfCancellationRequested();
            return new DriverSearchPassStatus(
                source, "Completed", Math.Max(0, count),
                count == 0 ? "Nessun candidato restituito dalla fonte." : "Ricerca eseguita.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WuaPartialSearchException exception)
        {
            onFailure?.Invoke(exception);
            return new DriverSearchPassStatus(
                source, "Partial", 0,
                "Windows Update ha restituito un esito parziale o avvisi; " +
                "risultati di questa fonte non convalidati. " +
                "La disponibilità di altri driver resta sconosciuta.");
        }
        catch (Exception exception)
        {
            onFailure?.Invoke(exception);
            return new DriverSearchPassStatus(
                source, "Failed", 0,
                "Interrogazione non riuscita; disponibilità driver non verificata.");
        }
    }
}
