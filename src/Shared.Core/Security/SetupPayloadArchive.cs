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
        // #519: explicit and implicit ZIP directories share a namespace
        // with file entries on Windows. Reject file-as-parent conflicts
        // in the preflight, before any staging directories/files exist.
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            filePaths.Add(name);
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

        // A valid entry path may have implicit parent directories. If any
        // such parent is also a ZIP file, extraction would fail only after
        // writing preceding entries (or collide with an essential EXE).
        // Compare with Windows case-insensitivity regardless of host OS.
        // This is bounded to at most MaxEntries paths and 16 components.
        foreach (var name in seen)
        {
            for (var slash = name.IndexOf('/'); slash >= 0;
                slash = name.IndexOf('/', slash + 1))
            {
                if (filePaths.Contains(name[..slash]))
                    throw new InvalidDataException(
                        "Conflitto file/cartella nel payload di installazione.");
            }
        }

        var root = Path.GetFullPath(destination);
        // A preexisting junction in the staging path must be rejected
        // BEFORE CreateDirectory can make descendants outside this root.
        SetupStagingDirectory.EnsureNoReparseAncestors(root);
        Directory.CreateDirectory(root);
        SetupStagingDirectory.EnsureNoReparseAncestors(root);
        using var stagingAnchor = SetupDirectoryAnchor.Open(root);
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
                // Pin each intermediate component rather than materialize
                // multiple missing parents in a single mkdir (#565).
                using var directoryPins = EnsurePinnedChildren(root, target);
                continue;
            }

            var parent = Path.GetDirectoryName(target)!;
            // Keep every nested directory pinned until the copy finishes.
            using var parentPins = EnsurePinnedChildren(root, parent);
            // #567: on Windows create the ZIP entry relative to the
            // already-pinned parent directory handle, never by reopening
            // an absolute path. No path-based Windows fallback is allowed.
            // FILE_CREATE still refuses preexisting files, links and dirs.
            using var output = OperatingSystem.IsWindows()
                ? WindowsHandleRelativeFile.CreateNew(
                    parentPins.LeafDirectoryHandle, Path.GetFileName(target))
                : new FileStream(target, FileMode.CreateNew,
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


    // The Windows branch creates/opens each child via its parent's native
    // handle. The non-Windows test branch keeps stepwise path traversal.
    // Root staging traversal and concurrency QA still belong to #529.
    private static DirectoryPins EnsurePinnedChildren(
        string root, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root)
            ? root : root + Path.DirectorySeparatorChar;
        if (!string.Equals(root, directory, comparison) &&
            !directory.StartsWith(prefix, comparison))
            throw new InvalidDataException(
                "Percorso fuori dall'area temporanea.");

        var pins = new DirectoryPins();
        try
        {
            pins.Add(SetupDirectoryAnchor.Open(root));
            var relative = Path.GetRelativePath(root, directory);
            if (relative == ".")
                return pins;

            var current = root;
            foreach (var component in relative.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or "..")
                    throw new InvalidDataException(
                        "Componente directory non consentito.");

                // Windows: create/open this component relative to the
                // verified parent handle, validate the returned handle and
                // hold a relative delete-on-close rename fence. Never
                // re-resolve this child via a full Windows path (#568).
                if (OperatingSystem.IsWindows())
                {
                    pins.Add(SetupDirectoryAnchor.OpenOrCreateChild(
                        pins.LeafDirectoryHandle, component));
                }
                else
                {
                    var child = Path.Combine(current, component);
                    SetupStagingDirectory.EnsureNoReparseAncestors(child);
                    Directory.CreateDirectory(child);
                    SetupStagingDirectory.EnsureNoReparseAncestors(child);
                    pins.Add(SetupDirectoryAnchor.Open(child));
                }
                current = Path.Combine(current, component);
            }
            return pins;
        }
        catch
        {
            pins.Dispose();
            throw;
        }
    }

    private sealed class DirectoryPins : IDisposable
    {
        private readonly List<SetupDirectoryAnchor> _anchors = [];

        public void Add(SetupDirectoryAnchor anchor) =>
            _anchors.Add(anchor);

        // The last anchor is the verified parent of the pending ZIP file.
        // Its handle remains open until extraction of that file completes.
        public Microsoft.Win32.SafeHandles.SafeFileHandle LeafDirectoryHandle =>
            _anchors.Count > 0 ? _anchors[^1].LeafDirectoryHandle :
                throw new InvalidOperationException(
                    "Parent di estrazione non disponibile.");

        public void Dispose()
        {
            for (var i = _anchors.Count - 1; i >= 0; --i)
                _anchors[i].Dispose();
            _anchors.Clear();
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
                 basename[3] is >= '1' and <= '9' or '¹' or '²' or '³'))
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

}
