using Microsoft.Extensions.Logging;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Progress;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class WindowsUpdateDriverProvider(
    ILogger<WindowsUpdateDriverProvider> logger) : IDriverSearchCoverageProvider
{
    private const string RecommendedSearchCriteria =
        "IsInstalled=0 and IsHidden=0 and Type='Driver'";

    private const string BroadSearchCriteria =
        "IsInstalled=0 and Type='Driver'";

    private const int WindowsUpdateServerSelection = 2;

    public async Task<IReadOnlyList<DriverUpdateInfo>> SearchAsync(
        DriverSearchMode mode,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        (await SearchWithCoverageAsync(mode, progress, cancellationToken)).Updates;

    public Task<DriverSearchReport> SearchWithCoverageAsync(
        DriverSearchMode mode,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => SearchCore(mode, progress, cancellationToken), cancellationToken);

    public Task<DriverInstallResult> InstallAsync(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updateIds);

        return Task.Run(
            () => InstallCore(
                updateIds,
                progress,
                cancellationToken),
            cancellationToken);
    }

    private DriverSearchReport SearchCore(
        DriverSearchMode mode,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Searching for driver updates in {SearchMode} mode.",
            mode);

        progress?.Invoke(new OperationProgress(
            "search",
            8,
            "Ricerca driver consigliati nel servizio Windows configurato..."));

        dynamic session =
            CreateComObject("Microsoft.Update.Session");

        session.ClientApplicationID =
            "OmegaDrive";

        var results =
            new Dictionary<string, DriverUpdateInfo>(
                StringComparer.OrdinalIgnoreCase);

        var sources = new List<DriverSearchPassStatus>();

        sources.Add(TrySearchPass(
            session,
            RecommendedSearchCriteria, null, false, false,
            "Windows configurato", results, cancellationToken));

        if (mode == DriverSearchMode.Comprehensive)
        {
            progress?.Invoke(new OperationProgress(
                "search", 42, "Ricerca online diretta su Windows Update..."));

            sources.Add(TrySearchPass(
                session, RecommendedSearchCriteria, WindowsUpdateServerSelection,
                false, false, "Windows Update online", results, cancellationToken));

            progress?.Invoke(new OperationProgress(
                "search", 68, "Ricerca candidati driver avanzati..."));

            sources.Add(TrySearchPass(
                session, BroadSearchCriteria, null, true, true,
                "Windows configurato · avanzato", results, cancellationToken));

            sources.Add(TrySearchPass(
                session, BroadSearchCriteria, WindowsUpdateServerSelection,
                true, true, "Windows Update online · avanzato",
                results, cancellationToken));
        }

        var list =
            results.Values
                .OrderBy(update => update.IsAdvancedCandidate)
                .ThenBy(update => update.IsHidden)
                .ThenBy(
                    update => update.Title,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        logger.LogInformation(
            "Driver search returned {UpdateCount} unique updates; " +
            "{AdvancedCount} advanced candidates, {HiddenCount} hidden.",
            list.Length,
            list.Count(update => update.IsAdvancedCandidate),
            list.Count(update => update.IsHidden));

        var report = new DriverSearchReport(list, sources);
        progress?.Invoke(new OperationProgress(
            "search", 100,
            report.AllFailed
                ? "Tutte le ricerche Windows Update sono fallite; disponibilità non verificata."
                : report.IsPartial
                    ? $"Ricerca parziale: {list.Length} candidati; alcune fonti non disponibili."
                    : $"Ricerca completata: {list.Length} candidati Windows Update."));

        return report;
    }

    private DriverSearchPassStatus TrySearchPass(
        dynamic session,
        string criteria,
        int? serverSelection,
        bool includePotentiallySuperseded,
        bool advancedCandidate,
        string searchSource,
        Dictionary<string, DriverUpdateInfo> results,
        CancellationToken cancellationToken)
    {
        // Commit only a fully successful pass; never leak partial enumerations
        // as verified data when COM fails midway through reading its updates.
        var staged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        var status = DriverSearchPassAudit.Run(
            searchSource,
            () => SearchPass(
                session, criteria, serverSelection,
                includePotentiallySuperseded, advancedCandidate,
                searchSource, staged, cancellationToken),
            cancellationToken,
            exception => logger.LogWarning(
                exception, "Driver search pass {SearchSource} failed.", searchSource));

        if (status.Succeeded)
        {
            foreach (var (updateId, candidate) in staged)
            {
                if (!results.TryGetValue(updateId, out var existing) ||
                    (existing.IsAdvancedCandidate && !candidate.IsAdvancedCandidate))
                {
                    results[updateId] = candidate;
                }
            }
        }

        return status;
    }

    private static int SearchPass(
        dynamic session,
        string criteria,
        int? serverSelection,
        bool includePotentiallySuperseded,
        bool advancedCandidate,
        string searchSource,
        Dictionary<string, DriverUpdateInfo> results,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        dynamic searcher =
            session.CreateUpdateSearcher();

        searcher.Online = true;
        searcher.IncludePotentiallySupersededUpdates =
            includePotentiallySuperseded;

        if (serverSelection is not null)
        {
            searcher.ServerSelection =
                serverSelection.Value;
        }

        dynamic result =
            searcher.Search(criteria);

        dynamic updates =
            result.Updates;

        var count =
            (int)updates.Count;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            dynamic update =
                updates.Item(index);

            dynamic identity =
                update.Identity;

            var updateId =
                (string)identity.UpdateID;

            var candidate =
                new DriverUpdateInfo(
                    updateId,
                    SafeString(update.Title) ?? "Driver Windows Update",
                    SafeString(update.Description),
                    SafeString(update.DriverClass),
                    SafeString(update.DriverProvider),
                    null,
                    DriverDownloadSizeResolver.Resolve(
                        SafeInt64(update.MaxDownloadSize),
                        SafeInt64(update.MinDownloadSize)),
                    SafeBool(update.IsDownloaded),
                    SafeString(update.DriverManufacturer),
                    SafeString(update.DriverModel),
                    SafeString(update.DriverHardwareID),
                    SafeDate(update.DriverVerDate),
                    SafeBool(update.IsHidden),
                    SafeBrowseOnly(update),
                    advancedCandidate,
                    searchSource);

            if (!results.TryGetValue(
                    updateId,
                    out var existing) ||
                (existing.IsAdvancedCandidate &&
                 !candidate.IsAdvancedCandidate))
            {
                results[updateId] =
                    candidate;
            }
        }

        return count;
    }

    private DriverInstallResult InstallCore(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (updateIds.Count == 0)
        {
            return new DriverInstallResult(
                false,
                false,
                "No driver update selected.");
        }

        logger.LogInformation(
            "Preparing installation of {UpdateCount} selected driver updates.",
            updateIds.Count);

        progress?.Invoke(new OperationProgress(
            "search",
            5,
            "Refreshing available driver updates..."));

        cancellationToken.ThrowIfCancellationRequested();

        dynamic session =
            CreateComObject("Microsoft.Update.Session");

        session.ClientApplicationID =
            "TheEasyWayForDrivers";

        dynamic selected =
            CreateComObject("Microsoft.Update.UpdateColl");

        var selectedIds =
            new HashSet<string>(
                updateIds,
                StringComparer.OrdinalIgnoreCase);

        var addedIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        AddSelectedUpdatesFromSearch(
            session,
            selectedIds,
            addedIds,
            selected,
            null,
            cancellationToken);

        if (addedIds.Count < selectedIds.Count)
        {
            try
            {
                AddSelectedUpdatesFromSearch(
                    session,
                    selectedIds,
                    addedIds,
                    selected,
                    WindowsUpdateServerSelection,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Direct Windows Update lookup for selected advanced drivers failed.");
            }
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

        dynamic downloader =
            session.CreateUpdateDownloader();

        downloader.Updates =
            selected;

        progress?.Invoke(new OperationProgress(
            "download",
            15,
            "Downloading selected drivers: 0%"));

        dynamic downloadResult =
            DownloadWithProgress(
                downloader,
                progress,
                cancellationToken);

        var downloadCode =
            (int)downloadResult.ResultCode;

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

        dynamic installer =
            session.CreateUpdateInstaller();

        installer.Updates =
            selected;

        dynamic installResult =
            InstallWithProgress(
                installer,
                progress,
                cancellationToken);

        var installCode =
            (int)installResult.ResultCode;

        var rebootRequired =
            (bool)installResult.RebootRequired;

        var succeeded =
            installCode is 2 or 3;

        logger.LogInformation(
            "Windows Update installation finished with result code {ResultCode}; " +
            "reboot required: {RebootRequired}.",
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

    private static void AddSelectedUpdatesFromSearch(
        dynamic session,
        HashSet<string> selectedIds,
        HashSet<string> addedIds,
        dynamic selected,
        int? serverSelection,
        CancellationToken cancellationToken)
    {
        dynamic searcher =
            session.CreateUpdateSearcher();

        searcher.Online = true;
        searcher.IncludePotentiallySupersededUpdates = true;

        if (serverSelection is not null)
        {
            searcher.ServerSelection =
                serverSelection.Value;
        }

        dynamic searchResult =
            searcher.Search(BroadSearchCriteria);

        dynamic available =
            searchResult.Updates;

        var count =
            (int)available.Count;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            dynamic update =
                available.Item(index);

            dynamic identity =
                update.Identity;

            var updateId =
                (string)identity.UpdateID;

            if (!selectedIds.Contains(updateId) ||
                !addedIds.Add(updateId))
            {
                continue;
            }

            if (!SafeBool(update.EulaAccepted))
            {
                update.AcceptEula();
            }

            selected.Add(update);
        }
    }

    private static dynamic DownloadWithProgress(
        dynamic downloader,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var callback =
            new WuaAutomationCallback();

        dynamic job =
            downloader.BeginDownload(
                callback,
                callback,
                null);

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
        var callback =
            new WuaAutomationCallback();

        dynamic job =
            installer.BeginInstall(
                callback,
                callback,
                null);

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

            var rawPercent =
                TryGetJobPercent(job);

            if (rawPercent != lastRawPercent)
            {
                lastRawPercent =
                    rawPercent;

                progress?.Invoke(new OperationProgress(
                    stage,
                    ProgressMapper.Map(
                        rawPercent,
                        startPercent,
                        endPercent),
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
            dynamic jobProgress =
                job.GetProgress();

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

    private static dynamic CreateComObject(
        string progId)
    {
        var type =
            Type.GetTypeFromProgID(
                progId,
                throwOnError: true)
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
            return value is null
                ? null
                : (string)value;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeDate(dynamic value)
    {
        try
        {
            if (value is DateTime dateTime)
            {
                return new DateTimeOffset(dateTime);
            }

            var converted =
                Convert.ToDateTime(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture);

            return new DateTimeOffset(converted);
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeBrowseOnly(dynamic update)
    {
        try
        {
            return Convert.ToBoolean(
                update.BrowseOnly,
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeBool(dynamic value)
    {
        try
        {
            return value is not null &&
                   Convert.ToBoolean(
                       value,
                       System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
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
