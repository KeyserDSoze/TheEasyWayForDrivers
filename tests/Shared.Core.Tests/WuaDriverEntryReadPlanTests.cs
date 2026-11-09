using TheEasyWayForDrivers.Core.Update;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class WuaDriverEntryReadPlanTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(2048)]
    [InlineData(2050)]
    [InlineData(2560)]
    public void SmallCollectionReadsEveryEntryInOriginalOrder(int count)
    {
        var indices = WuaDriverEntryReadPlan.SelectIndices(count);
        Assert.Equal(Enumerable.Range(0, count), indices);
    }

    [Theory]
    [InlineData(2561)]
    [InlineData(2700)]
    [InlineData(5000)]
    [InlineData(100000)]
    [InlineData(int.MaxValue)]
    public void LongCollectionPreservesPrefixAndProbesAcrossCompleteTail(int count)
    {
        var indices = WuaDriverEntryReadPlan.SelectIndices(count);
        Assert.Equal(WuaDriverEntryLimits.MaximumEntriesToInspect, indices.Count);
        Assert.Equal(Enumerable.Range(0, 2048), indices.Take(2048));
        Assert.Equal(2048, indices[2048]);
        Assert.Equal(count - 1, indices[^1]);
        Assert.All(indices, i => Assert.InRange(i, 0, count - 1));
        Assert.True(indices.SequenceEqual(indices.OrderBy(x => x)));
        Assert.Equal(indices.Count, indices.Distinct().Count());
        Assert.Equal(indices, WuaDriverEntryReadPlan.SelectIndices(count));
    }

    [Fact]
    public void FiveThousandEntriesSampleMiddleAndLastWithoutHugeAllocation()
    {
        var indices = WuaDriverEntryReadPlan.SelectIndices(5000);
        Assert.Contains(indices, i => i > 3000 && i < 4000);
        Assert.Contains(indices, i => i > 4000 && i < 4999);
        Assert.Contains(4999, indices);
        Assert.DoesNotContain(indices, i => i >= 5000 || i < 0);
    }

    [Fact]
    public void NegativeCountIsInvalidNotAnEmptyVerifiedCollection() =>
        Assert.Throws<InvalidDataException>(() =>
            WuaDriverEntryReadPlan.SelectIndices(-1));
}
