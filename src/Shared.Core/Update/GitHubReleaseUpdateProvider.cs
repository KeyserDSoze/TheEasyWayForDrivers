using System.Net.Http.Headers;
using System.Text.Json;
using TheEasyWayForDrivers.Core.Abstractions;
using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

public sealed class GitHubReleaseUpdateProvider(HttpClient httpClient) : IAppUpdateProvider
{
    public const string LatestReleaseUrl =
        "https://api.github.com/repos/KeyserDSoze/TheEasyWayForDrivers/releases/latest";

    public async Task<AppUpdateInfo?> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TheEasyWayForDrivers", currentVersion.ToString()));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = json.RootElement;
        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("The GitHub release has no tag_name.");

        var latestVersion = VersionParser.ParseTag(tagName);
        string? downloadUrl = null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(
                asset.GetProperty("name").GetString(),
                "TheEasyWayForDrivers-Setup.exe",
                StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var notes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
        return new AppUpdateInfo(currentVersion, latestVersion, tagName, downloadUrl, notes);
    }
}
