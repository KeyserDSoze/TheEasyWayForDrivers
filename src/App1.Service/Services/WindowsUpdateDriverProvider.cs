using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.ServiceApp.Services;

public sealed class WindowsUpdateDriverProvider : IDriverUpdateProvider
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

    private static IReadOnlyList<DriverUpdateInfo> SearchCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        return list
            .OrderBy(update => update.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static DriverInstallResult InstallCore(
        IReadOnlyCollection<string> updateIds,
        Action<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (updateIds.Count == 0)
        {
            return new DriverInstallResult(false, false, "No driver update selected.");
        }

        progress?.Invoke(new OperationProgress("search", 5, "Refreshing available driver updates..."));
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
            return new DriverInstallResult(false, false, "The selected updates are no longer available.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(new OperationProgress("download", 15, "Downloading selected drivers..."));

        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = selected;
        dynamic downloadResult = downloader.Download();

        var downloadCode = (int)downloadResult.ResultCode;
        if (downloadCode is not (2 or 3))
        {
            progress?.Invoke(new OperationProgress("download", 50, "Driver download failed."));
            return new DriverInstallResult(false, false, $"Windows Update download failed with result code {downloadCode}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(new OperationProgress("download", 55, "Download completed."));
        progress?.Invoke(new OperationProgress("install", 65, "Installing selected drivers..."));

        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = selected;
        dynamic installResult = installer.Install();

        var installCode = (int)installResult.ResultCode;
        var rebootRequired = (bool)installResult.RebootRequired;
        var succeeded = installCode is 2 or 3;

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

    private static dynamic CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)
            ?? throw new InvalidOperationException($"COM component '{progId}' is unavailable.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Unable to create COM component '{progId}'.");
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
            return value is null ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
