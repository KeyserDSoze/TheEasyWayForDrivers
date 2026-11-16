using TheEasyWayForDrivers.Core.Models;
using TheEasyWayForDrivers.Core.Update;

namespace TheEasyWayForDrivers.ServiceApp.Services;

/// <summary>
/// Read-only audit of a fully enumerated but INCOMPLETE WUA Type=Driver
/// result. The COM raw count and safe display-only title count must remain
/// distinct; neither count represents installable candidates.
/// </summary>
public static class WuaPartialPassAccounting
{
    public static DriverSearchPassStatus Record(
        DriverSearchPassStatus partial, int rawEnumerated,
        int safeTitles, int newlyStaged)
    {
        ArgumentNullException.ThrowIfNull(partial);
        if (partial.Outcome != "Partial" ||
            partial.NewUniqueCandidates != 0 ||
            rawEnumerated is < 0 or > WuaPartialDriverHintCollector.MaximumRawResultsPerPass ||
            safeTitles < 0 || safeTitles > rawEnumerated ||
            newlyStaged < 0 || newlyStaged > safeTitles)
            throw new InvalidDataException(
                "Conteggi ricerca WUA parziale non coerenti.");

        // Candidates is the underlying WUA Updates.Count, NOT the number
        // of validated titles and NEVER a confirmed driver GUID count.
        // A missing COM Title or rejected GUID is counted among discarded
        // display-only hints, without exposing any COM message or device ID.
        return partial with
        {
            Candidates = rawEnumerated,
            NewUniqueCandidates = 0,
            Detail = "Ricerca WUA PARZIALE: " +
                $"{rawEnumerated} voci COM enumerate, " +
                $"{safeTitles} titoli sicuri, " +
                $"{rawEnumerated - safeTitles} voci senza indizio leggibile, " +
                $"{newlyStaged} nuovi indizi non verificati " +
                "prima della selezione globale bilanciata (max 32). " +
                "I conteggi NON indicano driver disponibili o installabili."
        };
    }
}
