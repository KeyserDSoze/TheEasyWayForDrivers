namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// WUA Search returns an ISearchResult even when its OperationResultCode
/// reports errors. A successful COM call does not imply complete coverage.
/// Do not commit its candidate list unless Windows reports a clean search.
/// </summary>
public static class WuaSearchResultPolicy
{
    public const int Succeeded = 2;
    public const int SucceededWithErrors = 3;

    public static void RequireComplete(int resultCode, int warningCount)
    {
        if (warningCount < 0)
            throw new InvalidDataException("Numero di avvisi WUA non valido.");

        if (resultCode == Succeeded && warningCount == 0)
            return;

        if (resultCode == SucceededWithErrors ||
            (resultCode == Succeeded && warningCount > 0))
        {
            // Never echo warning message text or device identifiers into the
            // copyable report. Classifying the pass as Partial blocks bulk
            // update while leaving other completed passes available.
            throw new WuaPartialSearchException();
        }

        // 0=NotStarted, 1=InProgress, 4=Failed, 5=Aborted, unknown;
        // none can be reported as a successful search with zero drivers.
        throw new InvalidDataException("La ricerca WUA non è stata completata.");
    }
}

/// <summary>
/// Specific classification used by the audited search-pass boundary.
/// It deliberately carries no Windows COM warning strings or private IDs.
/// </summary>
public sealed class WuaPartialSearchException : IOException
{
    public WuaPartialSearchException()
        : base("Ricerca Windows Update conclusa con errori o avvisi.")
    {
    }
}
