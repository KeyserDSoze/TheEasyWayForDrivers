using System.Globalization;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Reads WUA IDownloadResult / IInstallationResult and every per-update
/// result. Aggregate ResultCode=3 is never a successful batch.
/// Verification is fail-closed if a COM result is missing or inconsistent.
/// No update IDs, titles, exception messages or HRESULTs leave this class.
/// </summary>
public static class WuaBatchResultVerifier
{
    public const int MaxBatchUpdates = 256;
    private const int Succeeded = 2;
    private const int SucceededWithErrors = 3;

    public sealed record Outcome(
        int Requested,
        int ConfirmedSucceeded,
        bool FullySucceeded,
        bool RebootRequired)
    {
        public int NotConfirmed => Requested - ConfirmedSucceeded;

        public string DownloadMessage =>
            $"Download Windows Update incompleto o non verificato: " +
            $"{ConfirmedSucceeded} riusciti, {NotConfirmed} non confermati. " +
            "Nessuna installazione avviata: ripetere la scansione prima di riprovare.";

        public string InstallMessage =>
            $"Installazione Windows Update parziale o non verificabile: " +
            $"{ConfirmedSucceeded} aggiornamenti con esito riuscito, " +
            $"{NotConfirmed} non confermati. " +
            "Alcuni driver potrebbero essere stati installati: " +
            "ripetere la scansione dopo l'eventuale riavvio.";
    }

    public static Outcome Read(
        object? result,
        int expectedUpdateCount,
        bool isInstallation,
        CancellationToken cancellationToken = default)
    {
        if (expectedUpdateCount is < 1 or > MaxBatchUpdates)
            throw new ArgumentOutOfRangeException(nameof(expectedUpdateCount));

        cancellationToken.ThrowIfCancellationRequested();

        var confirmed = 0;
        var rebootRequired = false;
        var rebootVerified = !isInstallation;
        var globalCode = -1;

        if (result is null)
            return new Outcome(expectedUpdateCount, 0, false, false);

        try
        {
            dynamic batch = result;
            globalCode = Convert.ToInt32(
                batch.ResultCode, CultureInfo.InvariantCulture);
            if (isInstallation)
            {
                rebootRequired = Convert.ToBoolean(
                    batch.RebootRequired, CultureInfo.InvariantCulture);
                rebootVerified = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The operation may have changed Windows' state even when COM
            // cannot report it. Never translate missing evidence to success.
            return new Outcome(expectedUpdateCount, 0, false, rebootRequired);
        }

        if (globalCode is not (Succeeded or SucceededWithErrors))
            return new Outcome(expectedUpdateCount, 0, false, rebootRequired);

        for (var index = 0; index < expectedUpdateCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                dynamic batch = result;
                dynamic updateResult = batch.GetUpdateResult(index);
                var code = Convert.ToInt32(
                    updateResult.ResultCode, CultureInfo.InvariantCulture);
                if (isInstallation)
                {
                    // WUA exposes a per-update reboot flag as well as the
                    // batch flag. Preserve either signal, even for failures.
                    rebootRequired |= Convert.ToBoolean(
                        updateResult.RebootRequired, CultureInfo.InvariantCulture);
                }
                if (code == Succeeded)
                    confirmed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new Outcome(expectedUpdateCount, confirmed,
                    false, rebootRequired);
            }
        }

        return new Outcome(
            expectedUpdateCount,
            confirmed,
            globalCode == Succeeded &&
                confirmed == expectedUpdateCount &&
                rebootVerified,
            rebootRequired);
    }
}
