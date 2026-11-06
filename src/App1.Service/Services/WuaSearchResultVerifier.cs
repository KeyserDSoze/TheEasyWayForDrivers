using System.Globalization;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Read-only validation of Windows Update Agent ISearchResult. Call before
/// enumerating Updates: COM Search() can return an object containing results
/// even if ResultCode indicates a partial or failed query.
/// </summary>
public static class WuaSearchResultVerifier
{
    public static void RequireComplete(object searchResult)
    {
        if (IsPartialButReadable(searchResult))
            throw new WuaPartialSearchException();
    }

    /// <summary>
    /// For read-only information only: this may return true when WUA
    /// supplies Updates with an INCOMPLETE search. Never authorize a
    /// DriverUpdateInfo/installation from this result. No warning messages
    /// or HRESULTs are read. Failed/aborted and unreadable results throw.
    /// </summary>
    public static bool IsPartialButReadable(object searchResult)
    {
        ArgumentNullException.ThrowIfNull(searchResult);
        dynamic result = searchResult;
        var code = Convert.ToInt32(
            result.ResultCode, CultureInfo.InvariantCulture);

        // A failed/aborted result does not need any further COM property
        // access. Never enumerate its Updates or treat zero as complete.
        if (code != WuaSearchResultPolicy.Succeeded &&
            code != WuaSearchResultPolicy.SucceededWithErrors)
        {
            WuaSearchResultPolicy.RequireComplete(code, 0);
            throw new InvalidDataException("Ricerca WUA non verificata.");
        }

        dynamic warnings = result.Warnings;
        var count = Convert.ToInt32(
            warnings.Count, CultureInfo.InvariantCulture);
        if (count < 0)
            throw new InvalidDataException("Numero di avvisi WUA non valido.");
        return code == WuaSearchResultPolicy.SucceededWithErrors || count > 0;
    }
}
