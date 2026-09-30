namespace TheEasyWayForDrivers.Core.Update;

public static class DriverDownloadSizeResolver
{
    public static long? Resolve(
        long? maxDownloadSize,
        long? minDownloadSize)
    {
        if (maxDownloadSize is > 0)
        {
            return maxDownloadSize;
        }

        if (minDownloadSize is > 0)
        {
            return minDownloadSize;
        }

        return null;
    }
}
