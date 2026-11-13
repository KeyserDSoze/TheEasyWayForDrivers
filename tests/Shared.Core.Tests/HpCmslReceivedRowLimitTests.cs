using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #446: an old/legacy HP CMSL payload must satisfy the same 1000-row
/// completeness gate as Get-SoftpaqList before category-fair display.
/// Metadata is not proof of a per-device update or installability.
/// </summary>
public sealed class HpCmslReceivedRowLimitTests
{
    private static string Driver(int index, string category =
        "Driver - Keyboard, Mouse and Input Devices") =>
        JsonSerializer.Serialize(new
        {
            Id = $"sp{index:D6}",
            Name = $"HP SoftPaq {index}",
            Category = category,
            Version = "1.0"
        });

    private static string Firmware(int index) =>
        JsonSerializer.Serialize(new
        {
            Id = $"fw{index:D6}",
            Name = $"Firmware {index}",
            Category = "Firmware"
        });

    private static string ParseArray(IEnumerable<string> records) =>
        "[" + string.Join(",", records) + "]";

    [Theory]
    [InlineData(999)]
    [InlineData(1000)]
    public void AtOrBelowReaderCapStillPreservesLateRareDriverCategories(
        int receivedCount)
    {
        var common = Enumerable.Range(1, receivedCount - 3)
            .Select(i => Driver(i));
        var last = new[]
        {
            Driver(200001, "Driver - Graphics"),
            Driver(200002, "Driver - Network"),
            Driver(200003, "Driver - Audio")
        };
        var parsed = HpSoftpaqCatalogParser.Parse(
            ParseArray(common.Concat(last)));

        Assert.Equal(HpSoftpaqCatalogParser.MaximumDriverResults,
            parsed.Count);
        Assert.Contains(parsed, row => row.Id == "sp200001");
        Assert.Contains(parsed, row => row.Id == "sp200002");
        Assert.Contains(parsed, row => row.Id == "sp200003");
        Assert.Equal(parsed.Count, parsed.Select(row => row.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OneThousandAndOneDriverRowsFailInsteadOfShowingOnly120()
    {
        var received = Enumerable.Range(1,
            HpSoftpaqCatalogParser.MaximumReceivedRows + 1)
            .Select(i => Driver(i));

        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.Parse(ParseArray(received)));
    }

    [Fact]
    public void OneThousandAndOneUnrelatedFirmwareRowsAlsoFailClosed()
    {
        var received = Enumerable.Range(1,
            HpSoftpaqCatalogParser.MaximumReceivedRows + 1)
            .Select(i => Firmware(i));

        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.Parse(ParseArray(received)));
    }

    [Fact]
    public void BogusRowsBeforeGenuineDriversCannotBypassOverallCap()
    {
        var skipped = Enumerable.Range(1, 1000)
            .Select(i => Firmware(i));
        var lateDriver = Driver(987654, "Driver - Graphics");

        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.Parse(
                ParseArray(skipped.Append(lateDriver))));
    }

    [Fact]
    public void ExactlyOneThousandWithLateDriverDoesNotHideValidResult()
    {
        var ignored = Enumerable.Range(1, 999)
            .Select(i => Firmware(i));
        var parsed = HpSoftpaqCatalogParser.Parse(ParseArray(
            ignored.Append(Driver(987654, "Driver - Graphics"))));

        var only = Assert.Single(parsed);
        Assert.Equal("sp987654", only.Id);
    }

    [Fact]
    public void DuplicateAndInvalidEntriesStillCountTowardInputCompleteness()
    {
        var duplicates = Enumerable.Range(1, 1001).Select(i =>
            JsonSerializer.Serialize(new
            {
                Id = "sp500001",
                Name = $"Row {i}",
                Category = i % 2 == 0 ? "Driver - Firmware" :
                    "Driver - Audio"
            }));
        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.Parse(ParseArray(duplicates)));
    }

    [Fact]
    public void ExistingSingleObjectEmptyAndNullResponsesStillWork()
    {
        Assert.Equal(1000, HpSoftpaqCatalogParser.MaximumReceivedRows);
        var one = HpSoftpaqCatalogParser.Parse(
            Driver(543210, "Driver - Network"));
        Assert.Equal("sp543210", Assert.Single(one).Id);
        Assert.Empty(HpSoftpaqCatalogParser.Parse("[]"));
        Assert.Empty(HpSoftpaqCatalogParser.Parse("null"));
    }

    [Fact]
    public void MoreThanTheJsonByteLimitCannotClaimVerifiedCoverage()
    {
        var hugeName = new string('A', 1024 * 1024);
        var payload = JsonSerializer.Serialize(new[]
        {
            new { Id = "sp12345", Name = hugeName, Category = "Driver" }
        });
        Assert.Throws<InvalidDataException>(() =>
            HpSoftpaqCatalogParser.Parse(payload));
    }

    [Fact]
    public void FirmwaresAndUnknownDriverPrefixesRemainExcluded()
    {
        var result = HpSoftpaqCatalogParser.Parse(
            ParseArray(new[]
            {
                Firmware(1),
                Driver(2, "Driver - BIOS"),
                Driver(3, "Driver - Firmware"),
                Driver(4, "Driver - Graphics")
            }));
        Assert.Equal("sp000004", Assert.Single(result).Id);
    }
}
