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
            return;
        }

        // ISearchResult.Warnings is an official, read-only collection.
        // We intentionally do not read, log, export or display its messages.
        dynamic warnings = result.Warnings;
        var count = Convert.ToInt32(
            warnings.Count, CultureInfo.InvariantCulture);
        WuaSearchResultPolicy.RequireComplete(code, count);
    }
}
