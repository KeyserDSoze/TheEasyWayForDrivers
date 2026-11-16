namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Read-only local OEM audit boundary: the selected package's leaf folder
/// is not enough; a parent directory may already be a junction/symlink.
/// This does not prevent concurrent replacement of filesystem ancestors.
/// </summary>
public static class OemLocalFolderBoundary
{
    public static DirectoryInfo RequirePlainLocalDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var path = Path.GetFullPath(directory);

        // Reject remote/UNC and extended Windows device paths. A local
        // package audit must not silently follow network-mounted folders.
        if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Utilizzare una cartella OEM locale, non un percorso di rete.");

        var root = new DirectoryInfo(path);
        if (!root.Exists)
            throw new InvalidDataException(
                "La cartella OEM non è disponibile per la verifica.");

        for (DirectoryInfo? current = root; current is not null;
             current = current.Parent)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current.FullName);
            }
            catch (Exception error) when (error is IOException or
                UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Do not copy a user-selected OEM path or personal folder
                // from exception details into the diagnostic message.
                throw new InvalidDataException(
                    "Impossibile verificare la gerarchia della cartella OEM.");
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException(
                    "La cartella OEM attraversa un collegamento non consentito.");
        }

        return root;
    }
}
