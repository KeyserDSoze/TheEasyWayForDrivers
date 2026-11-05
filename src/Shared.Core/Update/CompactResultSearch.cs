using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Searches only public, user-facing WUA result metadata. Device Instance IDs,
/// Hardware IDs and descriptions are deliberately excluded.
/// </summary>
public static class CompactResultSearch
{
    public static bool Matches(DriverUpdateInfo update, string? query)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (string.IsNullOrWhiteSpace(query))
            return true;

        var term = query.Trim();

        return Contains(update.Title, term) ||
               Contains(update.Provider, term) ||
               Contains(update.Manufacturer, term) ||
               Contains(update.Model, term) ||
               Contains(update.DriverClass, term) ||
               Contains(update.SearchSource, term);
    }

    private static bool Contains(string? text, string term) =>
        text?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
