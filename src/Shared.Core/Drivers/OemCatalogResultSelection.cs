namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// Deterministic, bounded presentation choice AFTER filtering and de-dup.
/// Only OEM-declared category text defines groups. This is not a ranking
/// of applicability and never permits OEM driver installation.
/// </summary>
internal static class OemCatalogResultSelection
{
    public static IReadOnlyList<T> FairByCategory<T>(
        IReadOnlyList<(T Entry, string Category)> verifiedDriverRows,
        int maximum)
    {
        ArgumentNullException.ThrowIfNull(verifiedDriverRows);
        if (maximum < 0)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        if (maximum == 0)
            return [];
        if (verifiedDriverRows.Count <= maximum)
            return verifiedDriverRows.Select(row => row.Entry).ToArray();

        // Fairness applies only if genuine, already validated driver rows
        // exceed the existing UI budget. Preserve the prior order for small
        // catalogs and never promote BIOS/firmware/software into drivers.
        var groups = verifiedDriverRows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.Category)
                ? "(categoria non specificata)" : row.Category.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Select(row => row.Entry).ToArray())
            .ToArray();

        var output = new List<T>(maximum);
        for (var depth = 0; output.Count < maximum; depth++)
        {
            var any = false;
            foreach (var group in groups)
            {
                if (depth >= group.Length)
                    continue;
                any = true;
                output.Add(group[depth]);
                if (output.Count == maximum)
                    break;
            }
            if (!any)
                break;
        }
        return output;
    }
}
