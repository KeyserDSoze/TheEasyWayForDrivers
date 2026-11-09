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
        WindowsDriverSearchPlan.RecommendedCriteria;

    private const string BroadSearchCriteria =
        WindowsDriverSearchPlan.BroadCriteria;

    private const int WindowsUpdateServerSelection =
        WindowsDriverSearchPlan.WindowsUpdateServer;

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
        // Distinct, NON-installable titles from fully enumerated but partial
        // WUA searches. Never merge into results/DriverUpdateInfo.
        var partialHints = new Dictionary<string, WuaPartialDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var partialOmittedIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var partialOmitted = 0;

        // Enumerate registered services without adding Microsoft Update or
        // altering the machine's configured Windows Update/WSUS policy.
        // Service registration failure does not stop other independent passes.
        var microsoftUpdateRegistered = false;
        var registrationFailed = false;
        if (mode == DriverSearchMode.Comprehensive)
        {
            try
            {
                microsoftUpdateRegistered = IsMicrosoftUpdateAlreadyRegistered(
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                registrationFailed = true;
                logger.LogWarning(
                    "Unable to inspect registered WUA services; Microsoft Update not searched (HRESULT 0x{HResult:X8}).",
                    unchecked((uint)exception.HResult));
            }
        }

        var plan = WindowsDriverSearchPlan.Create(mode, microsoftUpdateRegistered);
        for (var index = 0; index < plan.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pass = plan[index];
            progress?.Invoke(new OperationProgress(
                "search",
                10 + (int)(75.0 * index / Math.Max(1, plan.Count)),
                $"Ricerca driver: {pass.Name}..."));

            sources.Add(TrySearchPass(
                session, pass.Criteria, pass.ServerSelection,
                pass.IncludePotentiallySuperseded, pass.IsAdvancedCandidate,
                pass.Name, results, partialHints, partialOmittedIds,
                ref partialOmitted, cancellationToken, pass.ServiceId));
        }

        if (mode == DriverSearchMode.Comprehensive && !microsoftUpdateRegistered)
        {
            sources.Add(registrationFailed
                ? WindowsDriverSearchPlan.RegistrationFailureStatus()
                : WindowsDriverSearchPlan.NotRegisteredStatus());
        }

        // WUA's separate Type='Software' classification can include Intel
        // PROSet/Killer extensions and vendor helper components. Discover
        // only for informational review. NEVER merge them into DriverUpdateInfo
        // or the Update All installation pathway.
        // Keep the bounded, verified Type=Software rows from ALL completed
        // WUA sources until the final cross-vendor 32-item selection.
        // An early Intel-heavy pass must not hide later Killer/NVIDIA rows.
        var allSoftware = new Dictionary<string, WuaDriverRelatedSoftware>(
            StringComparer.OrdinalIgnoreCase);
        var softwareSources = new List<DriverSearchPassStatus>();
        var softwarePlan = WindowsDriverSearchPlan.CreateRelatedSoftware(
            mode, microsoftUpdateRegistered);
        for (var index = 0; index < softwarePlan.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pass = softwarePlan[index];
            progress?.Invoke(new OperationProgress(
                "search", 84 + (int)(12.0 * index / Math.Max(1, softwarePlan.Count)),
                $"Ricerca componenti software OEM (solo informativi): {pass.Name}..."));
            softwareSources.Add(TrySearchSoftwarePass(
                (object)session, pass, allSoftware, cancellationToken));
        }

        var fairSoftware = WuaDriverRelatedSoftwareClassifier.SelectFair(
            allSoftware.Values.ToArray());
        var software = fairSoftware.Hints;
        var softwareOmitted = fairSoftware.Omitted;
        // These counts describe the visible, unique hints attributed to each
        // completed pass; earlier pre-selection totals remain in Detail.
        softwareSources = softwareSources.Select(source =>
        {
            if (!source.Succeeded)
                return source;
            var visible = software.Count(hint =>
                string.Equals(hint.SearchSource, source.Source,
                    StringComparison.OrdinalIgnoreCase));
            return source with
            {
                NewUniqueCandidates = visible,
                Detail = source.Detail +
                    $" {visible} suggerimenti visibili nella selezione " +
                    $"bilanciata globale (max {WuaDriverRelatedSoftwareClassifier.MaxItems})."
            };
        }).ToList();

        // Independently query BOTH local datastore states. A previously
        // installed NVIDIA/Killer/Realtek driver was invisible when cache
        // review searched only IsInstalled=0. This remains historical,
        // non-installable metadata and NEVER contributes to DriverUpdateInfo.
        var cachedDriverHints = new Dictionary<string, WuaCachedDriverHint>(
            StringComparer.OrdinalIgnoreCase);
        var cachedDriverSources = new List<DriverSearchPassStatus>();
        var cachedOmitted = 0;
        if (mode == DriverSearchMode.Comprehensive)
        {
            var installedCache = new List<WuaCachedDriverHint>();
            var notInstalledCache = new List<WuaCachedDriverHint>();
            var cachePasses = WuaCachedDriverSearchPlan.Create();
            for (var cacheIndex = 0; cacheIndex < cachePasses.Count; cacheIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cachePass = cachePasses[cacheIndex];
                progress?.Invoke(new OperationProgress(
                    "search", 96 + cacheIndex,
                    $"Archivio Windows locale, sola lettura: {cachePass.Name}..."));
                var stagedCache = new List<WuaCachedDriverHint>();
                var unreadableCachedTitles = 0;
                var cachedStatus = DriverSearchPassAudit.Run(
                    cachePass.Name,
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        dynamic offlineSearcher = session.CreateUpdateSearcher();
                        // Online=false only; never change server/WSUS policy.
                        WuaCachedDriverHintReader.ConfigureOffline(
                            (object)offlineSearcher);
                        dynamic offlineResult =
                            offlineSearcher.Search(cachePass.Criteria);
                        WuaSearchResultVerifier.RequireComplete(
                            (object)offlineResult);
                        dynamic cachedCollection = offlineResult.Updates;
                        var rawCount = (int)cachedCollection.Count;
                        var parsed = WuaCachedDriverHintReader.ReadAll(
                            (object)cachedCollection, rawCount,
                            cancellationToken, cachePass.WasInstalled);
                        stagedCache.AddRange(parsed.Hints);
                        unreadableCachedTitles = parsed.UnreadableTitles;
                        return rawCount;
                    },
                    cancellationToken,
                    exception => logger.LogWarning(
                        "WUA offline local datastore pass {Source} failed (HRESULT 0x{HResult:X8}).",
                        cachePass.Name, unchecked((uint)exception.HResult)));

                if (cachedStatus.Succeeded)
                {
                    var target = cachePass.WasInstalled
                        ? installedCache : notInstalledCache;
                    target.AddRange(stagedCache);
                    cachedStatus = cachedStatus with
                    {
                        Detail = $"{cachedStatus.Candidates} record nell'archivio " +
                            $"Windows locale, {stagedCache.Count} titoli leggibili, " +
                            $"{unreadableCachedTitles} non leggibili. " +
                            "Solo storico: disponibilità attuale NON verificata."
                    };
                }
                cachedDriverSources.Add(cachedStatus);
            }

            // Balanced so neither 4000-entry side consumes the whole
            // 32-row UI preview; online confirmed GUIDs always win.
            WuaCachedDriverHintCollector.MergeBalanced(
                cachedDriverHints, notInstalledCache, installedCache,
                results.Keys, out cachedOmitted);
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

        var report = new DriverSearchReport(
            list, sources,
            software.OrderBy(item => item.Title,
                StringComparer.CurrentCultureIgnoreCase).ToArray(),
            softwareSources, softwareOmitted,
            partialHints.Values
                .Where(hint => !results.ContainsKey(hint.UpdateId))
                .OrderBy(hint => hint.Title,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            partialOmitted,
            cachedDriverHints.Values.OrderBy(item => item.Title,
                StringComparer.CurrentCultureIgnoreCase).ToArray(),
            cachedDriverSources,
            cachedOmitted);
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
        IDictionary<string, WuaPartialDriverHint> partialHints,
        ISet<string> partialOmittedIds,
        ref int partialOmitted,
        CancellationToken cancellationToken,
        string? serviceId = null)
    {
        // Commit only a fully successful pass; never leak partial enumerations
        // as verified data when COM fails midway through reading its updates.
        var staged = new Dictionary<string, DriverUpdateInfo>(
            StringComparer.OrdinalIgnoreCase);

        var partialStaged = new List<WuaPartialDriverHint>();
        var fullyEnumeratedPartial = false;
        var status = DriverSearchPassAudit.Run(
            searchSource,
            () =>
            {
                var enumeration = SearchPass(
                    session, criteria, serverSelection,
                    includePotentiallySuperseded, advancedCandidate,
                    searchSource, staged, partialStaged,
                    cancellationToken, serviceId);
                if (enumeration.Partial)
                {
                    // No hint is committed until the ENTIRE WUA COM Update
                    // collection was successfully enumerated. Keep the pass
                    // marked Partial, never Completed.
                    fullyEnumeratedPartial = true;
                    throw new WuaPartialSearchException();
                }

                return enumeration.Count;
            },
            cancellationToken,
            exception => logger.LogWarning(
                "Driver search pass {SearchSource} failed (HRESULT 0x{HResult:X8}).",
                searchSource, unchecked((uint)exception.HResult)));

        if (!status.Succeeded)
        {
            if (fullyEnumeratedPartial && status.Outcome == "Partial")
            {
                var hintAdded = WuaPartialDriverHintCollector.Merge(
                    partialHints, partialStaged, out var omitted,
                    partialOmittedIds);
                partialOmitted += omitted;
                return status with
                {
                    Candidates = partialStaged.Count,
                    Detail = "Ricerca WUA parziale: " +
                        $"{partialStaged.Count} titoli letti integralmente, " +
                        $"{hintAdded} nuovi indizi non verificati, " +
                        $"{omitted} oltre limite; " +
                        "NON sono aggiornamenti installabili."
                };
            }
            return status;
        }

        // Count only previously unseen update IDs. Two services returning the
        // same driver must never inflate the total. Candidate enumeration is
        // committed only if the entire underlying COM pass succeeded.
        var added = WindowsDriverCandidateMerge.Merge(results, staged.Values);
        return status with
        {
            NewUniqueCandidates = added,
            Detail = $"Fonte interrogata: {status.Candidates} risultati, " +
                     $"{added} nuovi ID distinti rispetto alle ricerche precedenti."
        };
    }

    private DriverSearchPassStatus TrySearchSoftwarePass(
        object session,
        WindowsDriverSearchPlan.Pass pass,
        IDictionary<string, WuaDriverRelatedSoftware> verified,
        CancellationToken cancellationToken)
    {
        // A failing COM query must not leak partial results.
        var staged = new List<WuaDriverRelatedSoftware>();
        var unreadableTitles = 0;
        var status = DriverSearchPassAudit.Run(
            pass.Name,
            () => SearchSoftwarePass(session, pass, staged,
                cancellationToken, out unreadableTitles),
            cancellationToken,
            exception => logger.LogWarning(
                "Read-only WUA software pass {Source} failed (HRESULT 0x{HResult:X8}).",
                pass.Name, unchecked((uint)exception.HResult)));
        if (!status.Succeeded)
            return status;

        // Only a COMPLETE source is allowed to add hints. The COM reader
        // caps raw rows to 4000/pass and there are at most three passes.
        // Deduplicate across sources before the fair global 32-row choice.
        var added = 0;
        foreach (var hint in staged)
        {
            if (verified.ContainsKey(hint.UpdateId))
                continue;
            if (verified.Count >=
                WuaDriverRelatedSoftwareClassifier.MaximumStagedItems)
                throw new InvalidDataException(
                    "Raccolta software WUA verificata oltre il limite.");
            verified.Add(hint.UpdateId, hint);
            added++;
        }
        return status with
        {
            NewUniqueCandidates = added,
            Detail = $"{status.Candidates} aggiornamenti Type=Software restituiti, " +
                $"{staged.Count} possibili componenti OEM dal titolo, " +
                $"{unreadableTitles} titoli COM non valutabili, " +
                $"{added} nuovi ID software distinti prima della selezione " +
                "globale; nessun driver installabile aggiunto."
        };
    }

    private static int SearchSoftwarePass(
        dynamic session,
        WindowsDriverSearchPlan.Pass pass,
        List<WuaDriverRelatedSoftware> staged,
        CancellationToken cancellationToken,
        out int unreadableTitles)
    {
        unreadableTitles = 0;
        cancellationToken.ThrowIfCancellationRequested();
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online = true;
        searcher.IncludePotentiallySupersededUpdates = false;
        if (pass.ServerSelection is { } server)
            searcher.ServerSelection = server;
        if (!string.IsNullOrEmpty(pass.ServiceId))
            searcher.ServiceID = pass.ServiceId;

        dynamic searchResult = searcher.Search(pass.Criteria);
        // Success HRESULT does not prove a complete WUA search.
        WuaSearchResultVerifier.RequireComplete((object)searchResult);
        dynamic matches = searchResult.Updates;
        var count = (int)matches.Count;
        if (count < 0 || count > WuaDriverRelatedSoftwareClassifier.MaxRawSearchResults)
            throw new InvalidDataException(
                "Troppe voci Software WUA: fonte non verificata.");

        // Stage complete, bounded per-update metadata in a testable
        // reader; the WUA collection or identity failing at any index
        // invalidates the pass. Optional Title failures skip only the
        // affected entry and do not erase earlier valid vendor hints.
        var read = WuaSourceMetadataReader.ReadSoftwareHints(
            (object)matches, count, pass.Name, cancellationToken);
        staged.AddRange(read.Hints);
        unreadableTitles = read.UnreadableTitles;
        return count;
    }

    private static (int Count, bool Partial) SearchPass(
        dynamic session,
        string criteria,
        int? serverSelection,
        bool includePotentiallySuperseded,
        bool advancedCandidate,
        string searchSource,
        Dictionary<string, DriverUpdateInfo> results,
        List<WuaPartialDriverHint> partialStaged,
        CancellationToken cancellationToken,
        string? serviceId)
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

        if (!string.IsNullOrEmpty(serviceId))
        {
            // ssOthers requires a registered, explicit service ID. The
            // searcher does not register/change system services.
            searcher.ServiceID = serviceId;
        }

        dynamic result =
            searcher.Search(criteria);

        // Even if WUA reports warnings, Updates may contain some titles.
        // These can ONLY become non-installable hints, and ONLY after full
        // bounded enumeration. They never enter the verified WUA list.
        var isPartial = WuaSearchResultVerifier.IsPartialButReadable(
            (object)result);

        dynamic updates = result.Updates;
        var count = (int)updates.Count;
        if (count is < 0 or > WuaPartialDriverHintCollector.MaximumRawResultsPerPass)
            throw new InvalidDataException(
                "Elenco risultati WUA oltre il limite: ricerca non verificata.");

        if (isPartial)
        {
            // Separate helper returns ONLY after full COM enumeration.
            // A partial pass can never create DriverUpdateInfo records.
            partialStaged.AddRange(WuaPartialResultReader.ReadAll(
                (object)updates, count, searchSource, cancellationToken));
            return (count, true);
        }

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic update = updates.Item(index);
            dynamic identity = update.Identity;
            var updateId = (string)identity.UpdateID;

            // A COM getter may throw before a SafeString(value) helper is
            // invoked. Optional fields must be read inside their OWN
            // exception boundary, otherwise a single inaccessible field
            // invalidates the entire fully enumerated WUA driver pass.
            var primaryHardwareId = WuaDriverCandidateReader.ReadPrimaryHardwareId(
                (object)update);
            var entryIds = WindowsDriverEntryIdReader.ReadAdditionalIdsWithCoverage(
                (object)update, primaryHardwareId, cancellationToken);
            var candidate = WuaDriverCandidateReader.Read(
                (object)update, updateId, primaryHardwareId,
                entryIds.AdditionalHardwareIds, entryIds.Truncated,
                advancedCandidate, searchSource);

            // Device entries are not separate update packages.
            WindowsDriverCandidateMerge.Merge(results, [candidate]);
        }

        return (count, false);
    }

    private static bool IsMicrosoftUpdateAlreadyRegistered(
        CancellationToken cancellationToken)
    {
        // Read-only COM enumeration. Never call AddService,
        // RegisterServiceWithAU, SetOption or any policy mutation.
        cancellationToken.ThrowIfCancellationRequested();
        dynamic manager = CreateComObject("Microsoft.Update.ServiceManager");
        dynamic services = manager.Services;
        // Continue past individually unreadable service COM entries.
        // A verified Microsoft Update GUID is enough to query it; when a
        // service was unreadable and MU was NOT found, registration status
        // remains unknown rather than falsely reporting "not registered".
        return WuaSourceMetadataReader.IsMicrosoftUpdateRegistered(
            (object)services, cancellationToken);
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

        // A batch is an exact set of previously displayed WUA update GUIDs.
        // Invalid or unbounded requests must not reach download/install.
        if (!WuaBatchSelectionGate.IsValidRequest(selectedIds))
            return new DriverInstallResult(false, false,
                "Selezione Windows Update non valida o troppo ampia: ripetere la scansione.");

        var addedIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        try
        {
            AddSelectedUpdatesFromSearch(
                session,
                selectedIds,
                addedIds,
                selected,
                null,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An incomplete configured WUA search must never be used for
            // installation; a clean independent Windows Update/MU pass can
            // still locate the selected GUIDs below.
            logger.LogWarning(exception,
                "Configured WUA recheck was incomplete; trying other sources.");
        }

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
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Direct Windows Update lookup for selected advanced drivers failed.");
            }
        }

        if (addedIds.Count < selectedIds.Count)
        {
            try
            {
                if (IsMicrosoftUpdateAlreadyRegistered(cancellationToken))
                {
                    AddSelectedUpdatesFromSearch(
                        session, selectedIds, addedIds, selected,
                        WindowsDriverSearchPlan.OtherServer,
                        cancellationToken,
                        WindowsDriverSearchPlan.MicrosoftUpdateServiceId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "Registered Microsoft Update lookup for selected drivers failed.");
            }
        }

        // Never silently install a SUBSET when an update disappears between
        // scan and click. WUA may still have the other GUIDs available.
        if (!WuaBatchSelectionGate.IsComplete(
                selectedIds, addedIds, (int)selected.Count))
        {
            logger.LogWarning(
                "Recheck found only {FoundCount} of {RequestedCount} selected WUA IDs.",
                addedIds.Count, selectedIds.Count);

            return new DriverInstallResult(
                false, false,
                "La ricerca Windows non ha confermato tutti gli aggiornamenti " +
                "selezionati. Nessun download o installazione avviati; " +
                "ripetere la scansione e confermare la nuova selezione.");
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

        // WUA orcSucceededWithErrors (3) is NOT success. Validate both the
        // batch and every IDownloadResult.GetUpdateResult(i) before Install().
        var downloaded = WuaBatchResultVerifier.Read(
            (object)downloadResult, selectedIds.Count,
            isInstallation: false, cancellationToken);

        if (!downloaded.FullySucceeded)
        {
            logger.LogWarning(
                "Driver download was not fully verified: {SuccessCount}/{ExpectedCount}.",
                downloaded.ConfirmedSucceeded, downloaded.Requested);

            progress?.Invoke(new OperationProgress(
                "download", 55,
                "Download parziale o non verificato; nessuna installazione avviata."));

            return new DriverInstallResult(
                false, false, downloaded.DownloadMessage);
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

        // Once Windows has attempted installation, never report clean
        // success for partial/unknown per-update results. A partial batch
        // can still have changed some drivers and require a reboot.
        var installed = WuaBatchResultVerifier.Read(
            (object)installResult, selectedIds.Count,
            isInstallation: true, CancellationToken.None);

        logger.LogInformation(
            "WUA install verification: complete={FullySucceeded}, " +
            "confirmed={ConfirmedCount}/{RequestedCount}, reboot={RebootRequired}.",
            installed.FullySucceeded, installed.ConfirmedSucceeded,
            installed.Requested, installed.RebootRequired);

        progress?.Invoke(new OperationProgress(
            "complete", 100,
            installed.FullySucceeded
                ? installed.RebootRequired
                    ? "Driver installati; Windows richiede il riavvio."
                    : "Driver installati; risultati verificati."
                : "Installazione parziale o esito non verificabile; ripetere la scansione."));

        return new DriverInstallResult(
            installed.FullySucceeded,
            installed.RebootRequired,
            installed.FullySucceeded
                ? "Tutti gli aggiornamenti driver selezionati risultano riusciti " +
                  "secondo Windows Update."
                : installed.InstallMessage);
    }

    private static void AddSelectedUpdatesFromSearch(
        dynamic session,
        HashSet<string> selectedIds,
        HashSet<string> addedIds,
        dynamic selected,
        int? serverSelection,
        CancellationToken cancellationToken,
        string? serviceId = null)
    {
        dynamic searcher =
            session.CreateUpdateSearcher();

        searcher.Online = true;
        // Installation must recheck the *ordinary* WUA offer set only.
        // Hidden and potentially superseded packages remain review-only.
        searcher.IncludePotentiallySupersededUpdates =
            WuaInstallRecheckPolicy.IncludePotentiallySuperseded;

        if (serverSelection is not null)
        {
            searcher.ServerSelection =
                serverSelection.Value;
        }

        if (!string.IsNullOrEmpty(serviceId))
        {
            searcher.ServiceID = serviceId;
        }

        dynamic searchResult =
            searcher.Search(WuaInstallRecheckPolicy.Criteria);
        // The install-time recheck is a separate integrity boundary:
        // partial/aborted WUA results must not be installed.
        WuaSearchResultVerifier.RequireComplete((object)searchResult);

        dynamic available =
            searchResult.Updates;

        var count =
            (int)available.Count;

        if (count is < 0 or > 4000)
            throw new InvalidDataException(
                "Risposta WUA troppo ampia per la verifica dei GUID.");

        // Stage this entire search before mutating the selected collection.
        // A COM enumeration failure cannot leave partially verified results
        // from a search that never completed.
        var staged = new List<(string Id, object Update)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic update = available.Item(index);
            dynamic identity = update.Identity;
            var updateId = (string)identity.UpdateID;
            if (!selectedIds.Contains(updateId) ||
                addedIds.Contains(updateId) ||
                !seen.Add(updateId))
                continue;

            // WUA Type=Driver + IsHidden=0 search is required but not
            // sufficient: exclude firmware/BIOS/UEFI and verify properties
            // again BEFORE EULA acceptance or any download. Reading a
            // required COM property must not silently default to safe.
            bool hidden = (bool)update.IsHidden;
            string? title = SafeString(update.Title);
            string? driverClass = SafeString(update.DriverClass);
            if (!WuaInstallRecheckPolicy.CanStage(
                    updateId, updateId, hidden, title, driverClass))
                continue;

            staged.Add((updateId, (object)update));
        }

        // Deduplicate only AFTER EULA acceptance and collection Add succeed;
        // a failed Add must not mark an update as selected.
        foreach (var candidate in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (addedIds.Contains(candidate.Id))
                continue;

            dynamic update = candidate.Update;
            if (!SafeBool(update.EulaAccepted))
                update.AcceptEula();

            selected.Add(update);
            addedIds.Add(candidate.Id);
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
