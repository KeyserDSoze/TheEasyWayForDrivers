using TheEasyWayForDrivers.Core.Update;
using TheEasyWayForDrivers.ServiceApp.Services;
using Xunit;

namespace TheEasyWayForDrivers.ServiceApp.Tests;

public sealed class WuaSearchResultVerifierTests
{
    public sealed class FakeWarnings(int count)
    {
        public int Count => count;
    }

    public sealed class FakeResult(int code, int warningCount)
    {
        public int ResultCode => code;
        public FakeWarnings Warnings { get; } = new(warningCount);
        public object Updates => throw new InvalidOperationException(
            "Must not enumerate Updates while validating search status");
    }

    public sealed class MissingStatus
    {
        public FakeWarnings Warnings { get; } = new(0);
    }

    public sealed class FailedWithoutWarningAccess
    {
        public int ResultCode => 4;
        public object Warnings => throw new InvalidOperationException(
            "Warnings getter is not needed for failed searches");
    }

    public sealed class WarningGetterFailure
    {
        public int ResultCode => 2;
        public object Warnings => throw new InvalidOperationException(
            "COM warnings not readable");
    }

    [Fact]
    public void CleanWuaResultCanBeReadWithoutEnumeratingUpdates() =>
        WuaSearchResultVerifier.RequireComplete(new FakeResult(2, 0));

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 5)]
    public void WuaPartialResultsAreRejectedBeforeReadingUpdates(
        int code, int warnings)
    {
        var exception = Assert.Throws<WuaPartialSearchException>(() =>
            WuaSearchResultVerifier.RequireComplete(
                new FakeResult(code, warnings)));
        Assert.DoesNotContain("private", exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(42)]
    public void FailedAbortedOrUnknownWuaResultsCannotBeCommitted(int code) =>
        Assert.Throws<InvalidDataException>(() =>
            WuaSearchResultVerifier.RequireComplete(new FakeResult(code, 0)));

    [Fact]
    public void FailedResultDoesNotRequireWarningEnumeration() =>
        Assert.Throws<InvalidDataException>(() =>
            WuaSearchResultVerifier.RequireComplete(
                new FailedWithoutWarningAccess()));

    [Fact]
    public void MissingComPropertiesAreNotAssumedSuccessful()
    {
        Assert.ThrowsAny<Exception>(() =>
            WuaSearchResultVerifier.RequireComplete(new MissingStatus()));
        Assert.Throws<InvalidOperationException>(() =>
            WuaSearchResultVerifier.RequireComplete(new WarningGetterFailure()));
    }

    [Fact]
    public void NullResultIsInvalid() =>
        Assert.Throws<ArgumentNullException>(() =>
            WuaSearchResultVerifier.RequireComplete(null!));
}
