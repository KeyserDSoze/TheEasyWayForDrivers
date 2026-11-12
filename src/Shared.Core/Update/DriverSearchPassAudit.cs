using System.Runtime.InteropServices;
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
        var attemptedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var count = query();
            cancellationToken.ThrowIfCancellationRequested();
            // Negative query counts mean corrupt/unknown COM evidence,
            // NOT a successful search returning zero driver candidates.
            if (count < 0)
                throw new InvalidDataException(
                    "Conteggio risultati della fonte non verificabile.");
            return new DriverSearchPassStatus(
                source, "Completed", count,
                count == 0 ? "Nessun candidato restituito dalla fonte." : "Ricerca eseguita.",
                AttemptedAtUtc: attemptedAtUtc);
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
                "La disponibilità di altri driver resta sconosciuta.",
                AttemptedAtUtc: attemptedAtUtc);
        }
        catch (Exception exception)
        {
            onFailure?.Invoke(exception);
            var (outcome, detail) = ClassifyFailure(exception);
            return new DriverSearchPassStatus(
                source, outcome, 0, detail,
                AttemptedAtUtc: attemptedAtUtc);
        }
    }

    // Only Microsoft-documented COM HRESULTs provide a specific failure
    // category. Never disclose exception text, local paths or WSUS URLs.
    private static (string Outcome, string Detail) ClassifyFailure(Exception exception)
    {
        if (exception is COMException com)
        {
            switch (unchecked((uint)com.ErrorCode))
            {
                // WU_E_USER_ACCESS_DISABLED, WU_E_WU_DISABLED,
                // WU_E_CALL_CANCELLED_BY_POLICY.
                case 0x80240025:
                case 0x8024002E:
                case 0x8024002F:
                    return ("PolicyRestricted",
                        "Accesso Windows Update limitato dai criteri; " +
                        "fonte non verificata, nessun conteggio zero deducibile.");

                // WU_E_NO_CONNECTION, HTTP 408/503/504, name resolution.
                // Proxy/server problems do not prove this PC is offline.
                case 0x8024001F:
                case 0x8024401C:
                case 0x80244022:
                case 0x80244023:
                case 0x8024402C:
                    return ("NetworkError",
                        "Rete, proxy o servizio di aggiornamento non raggiungibile; " +
                        "fonte non verificata (non prova PC offline).");
            }
        }
        return ("Failed",
            "Interrogazione non riuscita; disponibilità driver non verificata.");
    }
}
