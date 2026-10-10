using System.Text;

namespace TheEasyWayForDrivers.Core.Drivers;

public static class DriverInstallHistoryReport
{
    public static bool Matches(DriverInstallHistoryEntry entry, string? search, string? outcome)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrWhiteSpace(search) &&
            !(entry.DriverTitles?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true ||
              entry.Outcome?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true ||
              entry.Message?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true))
            return false;

        return outcome switch
        {
            "succeeded" => entry.Outcome == "Batch riuscito (servizio)",
            "failed" => entry.Outcome == "Batch non riuscito (servizio)",
            "interrupted" => entry.Outcome == "Operazione interrotta (esito driver sconosciuto)",
            "reboot" => entry.RebootRequired,
            _ => true
        };
    }

    public static string ToCsv(IEnumerable<DriverInstallHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        static string Cell(string? value)
        {
            var text = value ?? string.Empty;
            // Spreadsheet applications can execute formulas even in quoted CSV
            // cells. Prefix risky inputs with a literal apostrophe.
            if (text.TrimStart(' ', '\t', '\r', '\n') is { Length: > 0 } trimmed &&
                "=+-@".Contains(trimmed[0]))
                text = "'" + text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        var csv = new StringBuilder();
        csv.AppendLine("Data UTC;Driver richiesti;Esito batch;Riavvio;Messaggio");
        foreach (var entry in entries)
        {
            csv.Append(Cell(entry.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            csv.Append(';').Append(Cell(entry.DriverTitles));
            csv.Append(';').Append(Cell(entry.Outcome));
            csv.Append(';').Append(Cell(entry.RebootRequired ? "Si" : "No"));
            csv.Append(';').AppendLine(Cell(entry.Message));
        }

        return csv.ToString();
    }
}
