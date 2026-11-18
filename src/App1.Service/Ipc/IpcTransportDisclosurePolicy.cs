using System.Text.Json;

namespace TheEasyWayForDrivers.ServiceApp.Ipc;

/// <summary>
/// Trusted, non-identifying strings for the Desktop/Service named-pipe
/// boundary. Provider exception messages may contain user paths, PnP IDs,
/// machine names, WUA endpoints or arbitrary local request values.
/// The detailed message and stack must never be emitted in IPC or the
/// service's transport-level logs.
/// </summary>
public static class IpcTransportDisclosurePolicy
{
    public const string InvalidRequest = "Richiesta IPC non valida.";
    public const string OperationFailed =
        "Operazione IPC non completata. Verificare lo stato del servizio.";

    private static readonly HashSet<string> KnownCommands =
        new(StringComparer.Ordinal)
        {
            "scan", "search-updates", "search-report",
            "install-updates", "oem-providers",
            "check-app-update", "diagnostics"
        };

    public static string SafeCommandLabel(string? command) =>
        command is not null && KnownCommands.Contains(command)
            ? command
            : "comando non riconosciuto";

    public static string ToClientError(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error is ArgumentException or JsonException
            ? InvalidRequest
            : OperationFailed;
    }
}
