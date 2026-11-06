using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaPartialResultReaderTests
{
    public sealed class FakeWarnings(int count)
    {
        public int Count => count;
    }

    public sealed class FakeResult(int code, int warnings)
    {
        public int ResultCode => code;
        public FakeWarnings Warnings { get; } = new(warnings);
    }

    public sealed class FakeIdentity(string updateId)
    {
        public string UpdateID => updateId;
    }

    public sealed class FakeUpdate(string updateId, string title)
    {
        public FakeIdentity Identity { get; } = new(updateId);
        public string Title => title;
    }

    public sealed class FakeUpdates(
        IReadOnlyList<FakeUpdate> updates,
        int throwAtIndex = -1,
        Action<int>? beforeRead = null)
    {
        public int Count => updates.Count;
        public int Calls { get; private set; }

        public FakeUpdate Item(int index)
        {
            Calls++;
            beforeRead?.Invoke(index);
            if (index == throwAtIndex)
                throw new InvalidOperationException("PRIVATE_COM_HARDWARE_ID");
            return updates[index];
        }
    }

    private static FakeUpdates Two() => new FakeUpdates(
    [
        new(Guid.NewGuid().ToString("D"), "NVIDIA - Display"),
        new(Guid.NewGuid().ToString("D"), "Intel - Net")
    ]);

    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    public void OnlyCompletedWithErrorsOrWarningsAreClassifiedPartial(
        int resultCode, int warnings)
    {
        Assert.True(WuaSearchResultVerifier.IsPartialButReadable(
            new FakeResult(resultCode, warnings)));
        Assert.ThrowsAny<IOException>(() =>
            WuaSearchResultVerifier.RequireComplete(
                new FakeResult(resultCode, warnings)));
    }

    [Fact]
    public void CleanSearchIsNotPartialAndRequireCompleteStillSucceeds()
    {
        Assert.False(WuaSearchResultVerifier.IsPartialButReadable(
            new FakeResult(2, 0)));
        WuaSearchResultVerifier.RequireComplete(new FakeResult(2, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(42)]
    public void FailedAbortedOrUnknownSearchDoesNotAllowPartialReading(int code)
    {
        Assert.Throws<InvalidDataException>(() =>
            WuaSearchResultVerifier.IsPartialButReadable(
                new FakeResult(code, 3)));
    }

    [Fact]
    public void FullEnumerationReturnsOnlyNonInstallableHints()
    {
        var collection = Two();
        var results = WuaPartialResultReader.ReadAll(
            collection, collection.Count, "Windows Update online");

        Assert.Equal(2, results.Count);
        Assert.Equal(2, collection.Calls);
        Assert.All(results, hint =>
        {
            Assert.False(hint.IsInstallable);
            Assert.False(hint.IsVerified);
            Assert.Contains("NON installabile", hint.Detail);
        });
    }

    [Fact]
    public void MidEnumerationComFailureRejectsEntireCollection()
    {
        var collection = new FakeUpdates(
        [
            new(Guid.NewGuid().ToString("D"), "NVIDIA - Display"),
            new(Guid.NewGuid().ToString("D"), "Intel - Net")
        ], throwAtIndex: 1);

        var failure = Assert.ThrowsAny<Exception>(() =>
            WuaPartialResultReader.ReadAll(
                collection, collection.Count, "Windows configurato"));

        Assert.Contains("PRIVATE_COM_HARDWARE_ID",
            failure.ToString());
        // No completed result list exists to merge into the UI/report. COM
        // failures are logged only in local diagnostics by the caller.
    }

    [Fact]
    public void CancellationMidEnumerationPropagatesWithoutPartialHints()
    {
        using var cancellation = new CancellationTokenSource();
        var collection = new FakeUpdates(
        [
            new(Guid.NewGuid().ToString("D"), "NVIDIA - Display"),
            new(Guid.NewGuid().ToString("D"), "Intel - Net")
        ], beforeRead: index =>
        {
            if (index == 0)
                cancellation.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() =>
            WuaPartialResultReader.ReadAll(collection, collection.Count,
                "Windows configurato", cancellation.Token));
    }

    [Fact]
    public void CorruptGuidOrTitleIsSkippedWithoutManufacturingAvailability()
    {
        var collection = new FakeUpdates(
        [
            new("fake-guid", "NVIDIA - Display"),
            new(Guid.NewGuid().ToString("D"), "NVIDIA - Display\nPRIVATE"),
            new(Guid.NewGuid().ToString("D"), "Intel - Net")
        ]);
        var hints = WuaPartialResultReader.ReadAll(collection,
            collection.Count, "Windows configurato · avanzato");

        Assert.Single(hints);
        Assert.Equal("Intel - Net", hints[0].Title);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4001)]
    public void OversizeOrInvalidSearchCountsFailClosed(int count) =>
        Assert.Throws<InvalidDataException>(() =>
            WuaPartialResultReader.ReadAll(Two(), count,
                "Windows Update online"));
}
