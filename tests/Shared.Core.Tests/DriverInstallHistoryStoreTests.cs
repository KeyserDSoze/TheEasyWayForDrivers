using TheEasyWayForDrivers.Core.Drivers;
using Xunit;

namespace TheEasyWayForDrivers.Core.Tests;

public sealed class DriverInstallHistoryStoreTests
{
    [Fact]
    public void SaveAndReload_RetainsOutcomeAndRebootFlag()
    {
        var path = Path.Combine(Path.GetTempPath(), "omega-history-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new DriverInstallHistoryStore(path);
            Assert.Empty(store.Read());
            store.Add(new DriverInstallHistoryEntry(
                DateTimeOffset.UtcNow, "GPU update", "Servizio: completato", true, "Riavvio richiesto"));
            var entry = Assert.Single(store.Read());
            Assert.Equal("GPU update", entry.DriverTitles);
            Assert.True(entry.RebootRequired);
            Assert.Equal("Servizio: completato", entry.Outcome);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_PreservesInvalidJsonAndCreatesNewHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omega-history-" + Guid.NewGuid() + ".json");
        var backupPattern = Path.GetFileName(path) + ".corrupt-*";
        try
        {
            File.WriteAllText(path, "{ invalid json");
            var store = new DriverInstallHistoryStore(path);
            store.Add(new DriverInstallHistoryEntry(
                DateTimeOffset.UtcNow, "Network driver", "Batch non riuscito", false, "Errore"));
            Assert.Single(store.Read());
            var backups = Directory.GetFiles(Path.GetDirectoryName(path)!, backupPattern);
            Assert.Single(backups);
            Assert.Equal("{ invalid json", File.ReadAllText(backups[0]));
        }
        finally
        {
            File.Delete(path);
            foreach (var backup in Directory.GetFiles(Path.GetDirectoryName(path)!, backupPattern))
                File.Delete(backup);
        }
    }

    [Fact]
    public void Read_OrdersNewestFirstAndLimitsHistory()
    {
        var path = Path.Combine(Path.GetTempPath(), "omega-history-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new DriverInstallHistoryStore(path);
            for (var i = 0; i < 205; i++)
                store.Add(new DriverInstallHistoryEntry(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
                    $"Batch {i}", "Completato", false, "Esito batch"));
            var entries = store.Read();
            Assert.Equal(200, entries.Count);
            Assert.Equal("Batch 204", entries[0].DriverTitles);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
