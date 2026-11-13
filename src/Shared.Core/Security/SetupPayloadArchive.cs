using System.IO.Compression;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Fail-closed extraction of the installer-embedded Service/Desktop ZIP.
/// This is a runtime trust boundary, independent of the GitHub packaging
/// gate. It never executes or installs drivers from the archive.
/// </summary>
public static class SetupPayloadArchive
{
    public const int MaxEntries = 4096;
    public const long MaxArchiveBytes = 512L * 1024 * 1024;
    public const long MaxSingleFileBytes = 384L * 1024 * 1024;
    public const long MaxExtractedBytes = 1024L * 1024 * 1024;

    public static void Extract(Stream archive, string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        cancellationToken.ThrowIfCancellationRequested();

        if (!archive.CanRead || !archive.CanSeek || archive.Position != 0 ||
            archive.Length is < 1 or > MaxArchiveBytes)
            throw new InvalidDataException(
                "Archivio di installazione non verificabile o oltre il limite.");

        using var zip = new ZipArchive(archive, ZipArchiveMode.Read,
            leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        var files = 0;
        var serviceExe = false;
        var desktopExe = false;

        // Preflight the entire ZIP BEFORE writing any entry or stopping
        // Windows components. A bad last entry cannot leave partial payload.
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++files > MaxEntries)
                throw new InvalidDataException(
                    "Troppi file nell'archivio di installazione.");

            var name = entry.FullName;
            var isDirectory = name.EndsWith('/');
            if (!IsSafeName(name, isDirectory) ||
                !seen.Add(name.TrimEnd('/')) ||
                IsLink(entry) ||
                (isDirectory && entry.Length != 0))
                throw new InvalidDataException(
                    "Percorso o collegamento non consentito nell'archivio.");

            if (isDirectory)
                continue;

            if (entry.Length < 0 || entry.Length > MaxSingleFileBytes ||
                entry.CompressedLength < 0)
                throw new InvalidDataException(
                    "File di installazione oltre il limite.");

            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExtractedBytes)
                throw new InvalidDataException(
                    "Payload di installazione troppo grande.");

            if (string.Equals(name, "Service/App1.Service.exe",
                StringComparison.OrdinalIgnoreCase))
                serviceExe = entry.Length > 0;
            if (string.Equals(name, "Desktop/App2.Desktop.exe",
                StringComparison.OrdinalIgnoreCase))
                desktopExe = entry.Length > 0;
        }

        if (!serviceExe || !desktopExe)
            throw new InvalidDataException(
                "Payload del servizio o del desktop mancante.");

        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        EnsureNoReparseAncestors(root);
        var prefix = Path.EndsInDirectorySeparator(root)
            ? root : root + Path.DirectorySeparatorChar;

        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName;
            var target = Path.GetFullPath(Path.Combine(
                root, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(prefix,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new InvalidDataException(
                    "L'archivio punta fuori dall'area temporanea.");

            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                EnsureNoReparseAncestors(target);
                continue;
            }

            var parent = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(parent);
            EnsureNoReparseAncestors(parent);
            // CreateNew prevents an existing file, symlink or hardlink
            // in the staging area from being overwritten by an admin.
            using var output = new FileStream(target, FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            using var input = entry.Open();

            var buffer = new byte[81920];
            long copied = 0;
            int count;
            while ((count = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                copied = checked(copied + count);
                if (copied > entry.Length || copied > MaxSingleFileBytes)
                    throw new InvalidDataException(
                        "Contenuto ZIP più grande del valore dichiarato.");
                output.Write(buffer, 0, count);
            }
            if (copied != entry.Length)
                throw new InvalidDataException(
                    "Contenuto ZIP incompleto.");
        }
    }

    private static bool IsSafeName(string name, bool isDirectory)
    {
        if (name.Length is < 3 or > 1024 ||
            name.Contains('\\') || name.StartsWith('/') ||
            name.Contains(':') || name.Any(char.IsControl))
            return false;

        var path = isDirectory ? name[..^1] : name;
        var parts = path.Split('/');
        // Compress-Archive commonly includes explicit top-level
        // Service/ and Desktop/ directory entries.
        if ((parts.Length < 2 && !isDirectory) ||
            parts.Length > 16 ||
            (parts[0] != "Service" && parts[0] != "Desktop"))
            return false;

        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 255 ||
                part is "." or ".." ||
                part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                return false;

            // Windows reserves these device basenames even with suffixes.
            var basename = part.Split('.')[0];
            if (basename.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (basename.Length == 4 &&
                 (basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 basename[3] is >= '1' and <= '9'))
                return false;
        }
        return true;
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        // Unix ZIP mode: symlink=0120000; Windows DOS reparse point=0x400.
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixType == 0xA000 ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static void EnsureNoReparseAncestors(string path)
    {
        for (DirectoryInfo? dir = new(path); dir is not null;
             dir = dir.Parent)
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "La destinazione temporanea contiene un collegamento.");
        }
    }
}
