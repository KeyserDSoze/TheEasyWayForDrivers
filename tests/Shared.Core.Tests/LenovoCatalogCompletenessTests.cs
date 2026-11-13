using System.Text.Json;
using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

/// <summary>
/// #447: the Lenovo.Client.Scripting query must forward the whole bounded
/// MachineType catalog to the 50-row fair presentation, never just its
/// first fifty packages. These records are OEM metadata, not driver
/// applicability, WUA offers or installation authorization.
/// </summary>
public sealed class LenovoCatalogCompletenessTests
{
    private static string Item(int i, string category = "Audio",
        string type = "2", string? id = null) =>
        JsonSerializer.Serialize(new
        {
            ID = id ?? $"driver{i:D5}",
            Name = $"OEM catalog {i}",
            Category = category,
            PackageType = type,
            Version = "1.0",
            ReleaseDate = "2026-11-13"
        });

    private static string Array(IEnumerable<string> items) =>
        "[" + string.Join(",", items) + "]";

    [Theory]
    [InlineData(999)]
    [InlineData(1000)]
    public void UpToReaderLimitIncludesLateGraphicsNetworkAndAudio(
        int total)
    {
        var common = Enumerable.Range(0, total - 3)
            .Select(i => Item(i));
        var rare = new[]
        {
            Item(100001, "Graphics", id: "NVIDIA-GPU"),
            Item(100002, "Network", id: "Intel-Killer"),
            Item(100003, "Audio", id: "Realtek")
        };
        var parsed = LenovoCatalogParser.Parse(Array(common.Concat(rare)));

        Assert.Equal(LenovoCatalogParser.MaximumDriverResults, parsed.Count);
        Assert.Contains(parsed, row => row.Id == "NVIDIA-GPU");
        Assert.Contains(parsed, row => row.Id == "Intel-Killer");
        Assert.Contains(parsed, row => row.Id == "Realtek");
        Assert.Equal(parsed.Count, parsed.Select(row => row.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OneThousandAndOneDriverRowsFailExplicitly()
    {
        var rows = Enumerable.Range(0,
            LenovoCatalogParser.MaximumReceivedRows + 1)
            .Select(i => Item(i));

        Assert.Throws<InvalidDataException>(() =>
            LenovoCatalogParser.Parse(Array(rows)));
    }

    [Fact]
    public void NonDriversAndDuplicateIdsCannotHideOverflow()
    {
        var firmware = Enumerable.Range(0, 1000)
            .Select(i => Item(i, "Firmware", "4", "same-id"));
        var late = Item(100001, "Graphics", "2", "NVIDIA-GPU");

        Assert.Throws<InvalidDataException>(() =>
            LenovoCatalogParser.Parse(Array(firmware.Append(late))));
    }

    [Fact]
    public void ExactlyOneThousandWithLastActualDriverStillShowsIt()
    {
        var firmware = Enumerable.Range(0, 999)
            .Select(i => Item(i, "Firmware", "4", $"fw{i}"));
        var late = Item(100001, "Graphics", "2", "NVIDIA-GPU");

        var parsed = LenovoCatalogParser.Parse(
            Array(firmware.Append(late)));
        Assert.Equal("NVIDIA-GPU", Assert.Single(parsed).Id);
    }

    [Fact]
    public void NonDriverBeforeNvidiaKillerCannotSpendPreviewBudget()
    {
        var firmware = Enumerable.Range(0, 130)
            .Select(i => Item(i, "BIOS", "3"));
        var drivers = new[]
        {
            Item(300001, "Graphics", "2", "NVIDIA-GPU"),
            Item(300002, "Network", "2", "Killer-WiFi")
        };
        var parsed = LenovoCatalogParser.Parse(Array(
            firmware.Concat(drivers)));

        Assert.Equal(2, parsed.Count);
        Assert.Contains(parsed, row => row.Id == "NVIDIA-GPU");
        Assert.Contains(parsed, row => row.Id == "Killer-WiFi");
    }

    [Fact]
    public void SingleObjectNullEmptyAndOversizedJsonStillBehaveSafely()
    {
        Assert.Equal(1000, LenovoCatalogParser.MaximumReceivedRows);
        Assert.Single(LenovoCatalogParser.Parse(Item(101, "Network")));
        Assert.Empty(LenovoCatalogParser.Parse("null"));
        Assert.Empty(LenovoCatalogParser.Parse("[]"));
        Assert.Throws<InvalidDataException>(() =>
            LenovoCatalogParser.Parse(new string('x', 1024 * 1024 + 1)));
    }

    [Fact]
    public void ExplicitFirmwareAndBiosNeverTurnIntoDriverMetadata()
    {
        var parsed = LenovoCatalogParser.Parse(Array(new[]
        {
            Item(101, "Graphics", "3"),
            Item(102, "Network", "4"),
            Item(103, "Graphics", "2", "Genuine")
        }));
        Assert.Equal("Genuine", Assert.Single(parsed).Id);
    }
}
