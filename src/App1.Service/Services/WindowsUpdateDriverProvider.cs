using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Progress;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class WindowsUpdateDriverProvider(
    ILogger<WindowsUpdateDriverProvider> logger) : IDriverUpdateProvider
{
    private const string DriverSearchCriteria = "IsInstalled=0 and Type='Driver'";

    public Task<IReadOnlyList<DriverUpdateInfo>> SearchAsync(
        CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DriverUpdateInfo>>(
            () => SearchCore(cancellationToken),
            cancellationToken);
    }

    public Task<DriverInstallResult> InstallAsync(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updateIds);

        return Task.Run(
            () => InstallCore(updateIds, progress, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyList<DriverUpdateInfo> SearchCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Searching Windows Update for driver updates.");

        dynamic session = CreateComObject("Microsoft.Update.Session");
        session.ClientApplicationID = "TheEasyWayForDrivers";

        dynamic searcher = session.CreateUpdateSearcher();
        dynamic result = searcher.Search(DriverSearchCriteria);
        dynamic updates = result.Updates;

        var list = new List<DriverUpdateInfo>();
        var count = (int)updates.Count;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            dynamic update = updates.Item(index);
            dynamic identity = update.Identity;

            list.Add(new DriverUpdateInfo(
                (string)identity.UpdateID,
                (string)update.Title,
                SafeString(update.Description),
                null,
                null,
                null,
                SafeInt64(update.MaxDownloadSize),
                (bool)update.IsDownloaded));
        }

        logger.LogInformation(
            "Windows Update returned {UpdateCount} driver updates.",
            list.Count);

        return list
            .OrderBy(update => update.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private DriverInstallResult InstallCore(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (updateIds.Count == 0)
        {
            return new DriverInstallResult(false, false, "No driver update selected.");
        }

        logger.LogInformation(
            "Preparing installation of {UpdateCount} selected driver updates.",
            updateIds.Count);

        progress?.Invoke(new OperationProgress(
            "search",
            5,
            "Refreshing available driver updates..."));

        cancellationToken.ThrowIfCancellationRequested();

        dynamic session = CreateComObject("Microsoft.Update.Session");
        session.ClientApplicationID = "TheEasyWayForDrivers";

        dynamic searcher = session.CreateUpdateSearcher();
        dynamic searchResult = searcher.Search(DriverSearchCriteria);
        dynamic available = searchResult.Updates;
        dynamic selected = CreateComObject("Microsoft.Update.UpdateColl");

        var selectedIds = new HashSet<string>(updateIds, StringComparer.OrdinalIgnoreCase);
        var count = (int)available.Count;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            dynamic update = available.Item(index);
            dynamic identity = update.Identity;
            var updateId = (string)identity.UpdateID;

            if (!selectedIds.Contains(updateId))
            {
                continue;
            }

            if (!(bool)update.EulaAccepted)
            {
                update.AcceptEula();
            }

            selected.Add(update);
        }

        if ((int)selected.Count == 0)
        {
            logger.LogWarning(
                "None of the {UpdateCount} selected updates are still available.",
                updateIds.Count);

            return new DriverInstallResult(
                false,
                false,
                "The selected updates are no longer available.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = selected;

        progress?.Invoke(new OperationProgress(
            "download",
            15,
            "Downloading selected drivers: 0%"));

        dynamic downloadResult = DownloadWithProgress(
            downloader,
            progress,
            cancellationToken);

        var downloadCode = (int)downloadResult.ResultCode;
        if (downloadCode is not (2 or 3))
        {
            logger.LogError(
                "Windows Update driver download failed with result code {ResultCode}.",
                downloadCode);

            progress?.Invoke(new OperationProgress(
                "download",
                55,
                "Driver download failed."));

            return new DriverInstallResult(
                false,
                false,
                $"Windows Update download failed with result code {downloadCode}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Invoke(new OperationProgress(
            "install",
            60,
            "Installing selected drivers: 0%"));

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = selected;

        dynamic installResult = InstallWithProgress(
            installer,
            progress,
            cancellationToken);

        var installCode = (int)installResult.ResultCode;
        var rebootRequired = (bool)installResult.RebootRequired;
        var succeeded = installCode is 2 or 3;

        logger.LogInformation(
            "Windows Update installation finished with result code {ResultCode}; reboot required: {RebootRequired}.",
            installCode,
            rebootRequired);

        progress?.Invoke(new OperationProgress(
            "complete",
            100,
            rebootRequired
                ? "Installation completed. A reboot is required."
                : "Installation completed."));

        return new DriverInstallResult(
            succeeded,
            rebootRequired,
            succeeded
                ? "Selected driver updates were processed."
                : $"Windows Update installation failed with result code {installCode}.");
    }

    private static dynamic DownloadWithProgress(
        dynamic downloader,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var callback = new WuaAutomationCallback();
        dynamic job = downloader.BeginDownload(callback, callback, null);

        try
        {
            MonitorJob(
                job,
                "download",
                15,
                55,
                "Downloading selected drivers",
                progress,
                cancellationToken);

            return downloader.EndDownload(job);
        }
        catch (OperationCanceledException)
        {
            TryAbort(job);
            throw;
        }
        finally
        {
            GC.KeepAlive(callback);
        }
    }

    private static dynamic InstallWithProgress(
        dynamic installer,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var callback = new WuaAutomationCallback();
        dynamic job = installer.BeginInstall(callback, callback, null);

        try
        {
            MonitorJob(
                job,
                "install",
                60,
                95,
                "Installing selected drivers",
                progress,
                cancellationToken);

            return installer.EndInstall(job);
        }
        catch (OperationCanceledException)
        {
            TryAbort(job);
            throw;
        }
        finally
        {
            GC.KeepAlive(callback);
        }
    }

    private static void MonitorJob(
        dynamic job,
        string stage,
        int startPercent,
        int endPercent,
        string message,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var lastRawPercent = -1;

        while (!(bool)job.IsCompleted)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                TryAbort(job);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var rawPercent = TryGetJobPercent(job);
            if (rawPercent != lastRawPercent)
            {
                lastRawPercent = rawPercent;
                progress?.Invoke(new OperationProgress(
                    stage,
                    ProgressMapper.Map(rawPercent, startPercent, endPercent),
                    $"{message}: {rawPercent}%"));
            }

            if (cancellationToken.WaitHandle.WaitOne(250))
            {
                TryAbort(job);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        progress?.Invoke(new OperationProgress(
            stage,
            endPercent,
            $"{message}: 100%"));
    }

    private static int TryGetJobPercent(dynamic job)
    {
        try
        {
            dynamic jobProgress = job.GetProgress();
            return Math.Clamp(
                Convert.ToInt32(
                    jobProgress.PercentComplete,
                    System.Globalization.CultureInfo.InvariantCulture),
                0,
                100);
        }
        catch
        {
            return 0;
        }
    }

    private static void TryAbort(dynamic job)
    {
        try
        {
            job.RequestAbort();
        }
        catch
        {
        }
    }

    private static dynamic CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)
            ?? throw new InvalidOperationException(
                $"COM component '{progId}' is unavailable.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                $"Unable to create COM component '{progId}'.");
    }

    private static string? SafeString(dynamic value)
    {
        try
        {
            return value is null ? null : (string)value;
        }
        catch
        {
            return null;
        }
    }

    private static long? SafeInt64(dynamic value)
    {
        try
        {
            return value is null
                ? null
                : Convert.ToInt64(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
