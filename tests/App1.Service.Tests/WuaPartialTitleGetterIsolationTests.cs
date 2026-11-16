using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

/// <summary>
/// #514: an optional COM Title failure in an INCOMPLETE Type=Driver search
/// cannot erase other safely enumerated review-only hints or create offers.
/// Structural collection/identity failures still invalidate the whole pass.
/// </summary>
public sealed class WuaPartialTitleGetterIsolationTests
{
    private const string Source = "Windows Update online";

    private static string Id(int n) =>
        $"00000000-0000-4000-8000-{n:D12}";

    public sealed class FakeIdentity(
        string id, bool failGetter = false)
    {
        public string UpdateID => failGetter
            ? throw new InvalidOperationException("PRIVATE_IDENTITY")
            : id;
    }

    public sealed class FakeUpdate(
        string id, object? title,
        bool failTitle = false, bool failIdentity = false,
        bool cancelTitle = false, bool oomTitle = false,
        bool failId = false)
    {
        public FakeIdentity Identity => failIdentity
            ? throw new InvalidOperationException("PRIVATE_IDENTITY")
            : new FakeIdentity(id, failId);

        public object? Title
        {
            get
            {
                if (cancelTitle)
                    throw new OperationCanceledException("PRIVATE_CANCEL");
                if (oomTitle)
                    throw new OutOfMemoryException("PRIVATE_OOM");
                if (failTitle)
                    throw new InvalidOperationException("PRIVATE_TITLE");
                return title;
            }
        }
    }

    public sealed class FakeUpdates(
        IReadOnlyList<FakeUpdate> items,
        int failAt = -1, Action<int>? before = null)
    {
        public int Calls { get; private set; }
        public object Item(int index)
        {
            Calls++;
            before?.Invoke(index);
            if (index == failAt)
                throw new InvalidOperationException("PRIVATE_COLLECTION");
            return items[index];
        }
    }

    [Fact]
    public void UnreadableMiddleTitleKeepsEarlierAndLaterVendorHints()
    {
        var collection = new FakeUpdates([
            new FakeUpdate(Id(1), "NVIDIA - Display"),
            new FakeUpdate(Id(2), "PRIVATE_CONTENT", failTitle: true),
            new FakeUpdate(Id(3), "Intel Killer - Wireless"),
            new FakeUpdate(Id(4), "Realtek - Audio")
        ]);
        var result = WuaPartialResultReader.ReadAll(collection, 4, Source);

        Assert.Equal(4, collection.Calls);
        Assert.Equal(3, result.Count);
        Assert.Equal([Id(1), Id(3), Id(4)],
            result.Select(row => row.UpdateId).ToArray());
        Assert.Contains(result, row => row.Title.Contains("Killer"));
        Assert.Contains(result, row => row.Title.Contains("Realtek"));
        Assert.All(result, row =>
        {
            Assert.False(row.IsInstallable);
            Assert.False(row.IsVerified);
            Assert.Contains("NON installabile", row.Detail);
        });
        Assert.DoesNotContain("PRIVATE_", string.Join(" ",
            result.Select(row => row.Title + row.Detail)));

        var report = new DriverSearchReport([],
            [new DriverSearchPassStatus(
                Source, "Partial", 4, "PRIVATE_LOCAL_ERROR")],
            PartialDriverHints: result);
        Assert.Empty(report.Updates);
        Assert.True(report.IsPartial);
        Assert.Empty(WindowsDriverCandidateMerge.ConfirmedForReport(report));
        Assert.Empty(QuickUpdatePolicy.AllOrdinaryIds(report.Updates));
        Assert.Equal(3, WuaPartialDriverHintCollector
            .ConfirmedForReport(report).Count);
    }

    [Fact]
    public void NonStringNullUnsafeAndMissingTitlesAreSkippedIndividually()
    {
        var collection = new FakeUpdates([
            new FakeUpdate(Id(1), 12345),
            new FakeUpdate(Id(2), null),
            new FakeUpdate(Id(3), "PRIVATE\nunsafe"),
            new FakeUpdate(Id(4), new object()),
            new FakeUpdate(Id(5), "NVIDIA - Display")
        ]);
        var hints = WuaPartialResultReader.ReadAll(collection, 5, Source);
        Assert.Equal(5, collection.Calls);
        Assert.Equal(Id(5), Assert.Single(hints).UpdateId);
        Assert.DoesNotContain("PRIVATE", hints[0].Title);
    }

    [Theory]
    [InlineData("Item")]
    [InlineData("Identity")]
    [InlineData("UpdateID")]
    public void StructuralFailureStillInvalidatesEntirePartialPass(
        string failure)
    {
        var items = new FakeUpdates([
            new FakeUpdate(Id(1), "NVIDIA - Display"),
            new FakeUpdate(Id(2), "Intel Killer",
                failIdentity: failure == "Identity",
                failId: failure == "UpdateID"),
            new FakeUpdate(Id(3), "Realtek - Audio")
        ], failAt: failure == "Item" ? 1 : -1);

        var error = Assert.ThrowsAny<Exception>(() =>
            WuaPartialResultReader.ReadAll(items, 3, Source));
        Assert.Contains("PRIVATE_", error.ToString());
        // No completed IReadOnlyList was returned; the caller must not
        // commit partially enumerated hints from this search.
    }

    [Fact]
    public void CancellationFromOptionalTitleIsNeverSwallowed()
    {
        var items = new FakeUpdates([
            new FakeUpdate(Id(1), "NVIDIA - Display"),
            new FakeUpdate(Id(2), "Intel Killer", cancelTitle: true),
            new FakeUpdate(Id(3), "Realtek - Audio")
        ]);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            WuaPartialResultReader.ReadAll(items, 3, Source));
        Assert.Equal(2, items.Calls);
    }

    [Fact]
    public void OutOfMemoryFromOptionalTitleIsNeverSwallowed()
    {
        var items = new FakeUpdates([
            new FakeUpdate(Id(1), "NVIDIA - Display", oomTitle: true),
            new FakeUpdate(Id(2), "Intel Killer")
        ]);
        Assert.Throws<OutOfMemoryException>(() =>
            WuaPartialResultReader.ReadAll(items, 2, Source));
        Assert.Equal(1, items.Calls);
    }

    [Fact]
    public void CancellationBetweenEntriesIsNotACompletedPartialRead()
    {
        using var stop = new CancellationTokenSource();
        var items = new FakeUpdates([
            new FakeUpdate(Id(1), "NVIDIA - Display"),
            new FakeUpdate(Id(2), "Intel Killer"),
            new FakeUpdate(Id(3), "Realtek - Audio")
        ], before: index =>
        {
            if (index == 1)
                stop.Cancel();
        });
        Assert.ThrowsAny<OperationCanceledException>(() =>
            WuaPartialResultReader.ReadAll(
                items, 3, Source, stop.Token));
    }

    [Fact]
    public void LimitCheckRemainsBeforeComItemAccess()
    {
        var items = new FakeUpdates([
            new FakeUpdate(Id(1), "NVIDIA - Display")
        ]);
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialResultReader.ReadAll(
                items, WuaPartialDriverHintCollector.MaximumRawResultsPerPass + 1,
                Source));
        Assert.Equal(0, items.Calls);
    }
}
