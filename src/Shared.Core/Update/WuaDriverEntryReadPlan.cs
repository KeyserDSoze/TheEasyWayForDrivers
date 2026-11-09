namespace TheEasyWayForDrivers.Core.Update;

/// <summary>
/// Bounded read-only index plan for WUA IWindowsDriverUpdateEntry COM data.
/// Preserve the first 2048 entries that previously were always inspected.
/// If the collection is larger than 2560, distribute 512 extra probes
/// across the remainder, including its last entry. Sampling is incomplete
/// evidence, NEVER proof that unvisited hardware IDs are absent.
/// </summary>
public static class WuaDriverEntryReadPlan
{
    public static IReadOnlyList<int> SelectIndices(int count)
    {
        if (count < 0)
            throw new InvalidDataException("Numero di entry WUA non valido.");

        if (count <= WuaDriverEntryLimits.MaximumEntriesToInspect)
            return Enumerable.Range(0, count).ToArray();

        var indices = new int[WuaDriverEntryLimits.MaximumEntriesToInspect];
        var prefix = WuaDriverEntryLimits.SequentialEntriesToInspect;
        var tail = WuaDriverEntryLimits.AdditionalTailProbes;

        for (var i = 0; i < prefix; i++)
            indices[i] = i;

        // Arithmetic is in long: a COM collection could report int.MaxValue
        // without allocating a huge list or overflowing the index spacing.
        var span = (long)count - 1 - prefix;
        for (var probe = 0; probe < tail; probe++)
            indices[prefix + probe] =
                checked((int)(prefix + (long)probe * span / (tail - 1)));

        return indices;
    }
}
