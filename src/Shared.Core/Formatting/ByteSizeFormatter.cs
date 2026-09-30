namespace TheEasyWayForDrivers.Core.Formatting;

public static class ByteSizeFormatter
{
    public static string Format(
        long? sizeBytes)
    {
        if (sizeBytes is null or <= 0)
        {
            return "N/D";
        }

        var size = sizeBytes.Value;

        if (size < 1024)
        {
            return $"{size} B";
        }

        if (size < 1024L * 1024L)
        {
            return $"{size / 1024d:N1} KB";
        }

        if (size < 1024L * 1024L * 1024L)
        {
            return $"{size / 1024d / 1024d:N1} MB";
        }

        return $"{size / 1024d / 1024d / 1024d:N2} GB";
    }
}
