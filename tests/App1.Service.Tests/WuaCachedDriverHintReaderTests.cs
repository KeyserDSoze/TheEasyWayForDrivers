using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaCachedDriverHintReaderTests
{
    public sealed class OfflineSearcher
    {
        public bool Online { get; set; } = true;
        public bool IncludePotentiallySupersededUpdates { get; set; }
        public int ServerSelection { get; set; } = 71;
        public string ServiceID { get; set; } = "PRIVATE_WSUS_POLICY";
    }

    public sealed class Identity(string id)
    {
        public string UpdateID => id;
    }
    public sealed class Entry(string id, string title)
    {
        public Identity Identity { get; } = new(id);
        public string Title => title;
    }
    public sealed class TitleFailure(string id)
    {
        public Identity Identity { get; } = new(id);
        public string Title => throw new InvalidOperationException(
            "Synthetic optional COM Title failure");
    }
    public sealed class Items(IReadOnlyList<object> entries, int? failAt = null,
        Action<int>? onRead = null)
    {
        public int Count => entries.Count;
        public object Item(int index)
        {
            onRead?.Invoke(index);
            if (index == failAt)
                throw new InvalidOperationException("Synthetic broken COM collection");
            return entries[index];
        }
    }

    private static string GuidAt(int i) =>
        $"00000000-0000-4000-8000-{i:D12}";

    [Fact]
    public void SearcherIsExplicitlyOfflineAndDoesNotTouchRegisteredServicePolicy()
    {
        var fake = new OfflineSearcher();
        WuaCachedDriverHintReader.ConfigureOffline(fake);
        Assert.False(fake.Online);
        Assert.True(fake.IncludePotentiallySupersededUpdates);
        Assert.Equal(71, fake.ServerSelection);
        Assert.Equal("PRIVATE_WSUS_POLICY", fake.ServiceID);
        Assert.Throws<ArgumentNullException>(() =>
            WuaCachedDriverHintReader.ConfigureOffline(null!));
    }

    [Fact]
    public void ReadsCachedNvidiaKillerAndRealtekOnlyAfterWholeCollection()
    {
        var rows = new object[]
        {
            new Entry(GuidAt(1), "NVIDIA - Display - 32.0.16.2101"),
            new Entry(GuidAt(2), "Intel - Killer Net - 24.80.0.2"),
            new Entry(GuidAt(3), "Realtek - MEDIA - 6.0.9661.1")
        };
        var result = WuaCachedDriverHintReader.ReadAll(
            new Items(rows), rows.Length);
        Assert.Equal(3, result.Hints.Count);
        Assert.Equal(0, result.UnreadableTitles);
        Assert.All(result.Hints, hint => Assert.False(hint.IsInstallable));
        Assert.Contains(result.Hints, hint => hint.Title.Contains("NVIDIA"));
        Assert.Contains(result.Hints, hint => hint.Title.Contains("Killer"));
        Assert.Contains(result.Hints, hint => hint.Title.Contains("Realtek"));
    }

    [Fact]
    public void FailedOptionalTitleSkipsOnlyAffectedCacheRow()
    {
        var list = new object[]
        {
            new Entry(GuidAt(1), "NVIDIA - Display"),
            new TitleFailure(GuidAt(2)),
            new Entry(GuidAt(3), "Intel - Wireless")
        };
        var outcome = WuaCachedDriverHintReader.ReadAll(
            new Items(list), list.Length);
        Assert.Equal(2, outcome.Hints.Count);
        Assert.Equal(1, outcome.UnreadableTitles);
    }

    [Fact]
    public void BrokenItemOrMalformedMandatoryIdentityRejectEntireCachePass()
    {
        object[] entries =
        [
            new Entry(GuidAt(1), "NVIDIA - Display"),
            new Entry(GuidAt(2), "Intel Killer"),
            new Entry(GuidAt(3), "Realtek Audio")
        ];
        Assert.Throws<InvalidOperationException>(() =>
            WuaCachedDriverHintReader.ReadAll(
                new Items(entries, failAt: 1), entries.Length));
        Assert.Throws<InvalidDataException>(() =>
            WuaCachedDriverHintReader.ReadAll(
                new Items([new Entry("not-a-guid", "NVIDIA")]), 1));
        Assert.Throws<InvalidDataException>(() =>
            WuaCachedDriverHintReader.ReadAll(
                new Items(entries), 4001));
        Assert.Throws<InvalidDataException>(() =>
            WuaCachedDriverHintReader.ReadAll(
                new Items(entries), -1));
    }

    [Fact]
    public void CancellationAtLaterEntryNeverReturnsPartialCacheEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        var items = Enumerable.Range(1, 100)
            .Select(i => (object)new Entry(GuidAt(i), "NVIDIA - Display"))
            .ToArray();
        var collection = new Items(items, onRead: index =>
        {
            if (index == 60)
                cancellation.Cancel();
        });
        Assert.Throws<OperationCanceledException>(() =>
            WuaCachedDriverHintReader.ReadAll(
                collection, items.Length, cancellation.Token));
    }

    [Fact]
    public void SuccessWithWarningsWouldNotBeAllowedToProduceCacheHints()
    {
        // The production reader is called ONLY after
        // WuaSearchResultVerifier.RequireComplete. This is a separate
        // fixture of the complete-search policy shared with online WUA.
        Assert.Throws<WuaPartialSearchException>(() =>
            WuaSearchResultPolicy.RequireComplete(
                WuaSearchResultPolicy.SucceededWithErrors, 0));
    }
}
