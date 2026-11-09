using System.IO;
using System.Text;
using TheEasyWayForDrivers.Core.Drivers;

namespace TheEasyWayForDrivers.Desktop.Services;

/// <summary>
/// User-selected saved HTML only. No browser automation, HTTP requests,
/// order entry, credential handling, download or script execution.
/// </summary>
public static class PcSpecialistPortalSnapshotReader
{
    public static async Task<PcSpecialistPortalSnapshot.Report> ReadAsync(
        string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists ||
            file.Extension is not ".html" and not ".htm" ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
            file.Length is < 64 or > PcSpecialistPortalSnapshot.MaximumHtmlCharacters)
            throw new InvalidDataException(
                "Selezionare un file HTML locale valido, senza collegamenti, " +
                "di dimensione inferiore a 1 MiB.");

        await using var input = new FileStream(file.FullName, FileMode.Open,
            FileAccess.Read, FileShare.Read, 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(input,
            new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[8192];
        var content = new StringBuilder();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var n = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (n == 0)
                break;
            if (content.Length + n > PcSpecialistPortalSnapshot.MaximumHtmlCharacters)
                throw new InvalidDataException(
                    "HTML PCSpecialist oltre il limite di dimensione.");
            content.Append(buffer, 0, n);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return PcSpecialistPortalSnapshot.Parse(content.ToString(), cancellationToken);
    }
}
