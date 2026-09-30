namespace TheEasyWayForDrivers.Core.Progress;

public static class ProgressMapper
{
    public static int Map(int percent, int start, int end)
    {
        if (start is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end is < 0 or > 100 || end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        var normalized = Math.Clamp(percent, 0, 100);
        return start + ((end - start) * normalized / 100);
    }
}
