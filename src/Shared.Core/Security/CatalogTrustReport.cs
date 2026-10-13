using System.Text.Json;

namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Parses the output of Windows Get-AuthenticodeSignature for a selected CAT.
/// This never establishes membership of an INF or SYS file in that catalog.
/// </summary>
public static class CatalogTrustReport
{
    public enum Status
    {
        ValidCatalogSignature,
        InvalidSignature,
        NotVerified
    }

    public sealed record Result(Status SignatureStatus, string WindowsStatus, string Signer)
    {
        public string Explanation => SignatureStatus switch
        {
            Status.ValidCatalogSignature =>
                "Windows segnala una firma Authenticode CAT valida. Appartenenza dell'INF e dei file driver al CAT NON verificata.",
            Status.InvalidSignature =>
                $"Firma CAT non valida o assente ({WindowsStatus}). Non usare questo pacchetto.",
            _ => $"Firma CAT non verificabile ({WindowsStatus}). Non usare questo risultato come prova di integrità."
        };
    }

    public static Result Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024)
            throw new InvalidDataException("Risposta verifica firma CAT assente o troppo grande.");

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Status", out var statusValue) ||
            statusValue.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Risposta Windows Authenticode non riconosciuta.");

        var status = statusValue.GetString() ?? "";
        if (status.Length > 40 || status.Any(c => !char.IsAsciiLetter(c)))
            throw new InvalidDataException("Stato firma CAT inatteso.");

        var signer = root.TryGetProperty("Signer", out var cert) &&
                     cert.ValueKind == JsonValueKind.String
            ? cert.GetString() ?? ""
            : "";
        signer = signer.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (signer.Length > 200) signer = signer[..200];

        var result = status switch
        {
            "Valid" when signer.Length > 0 => Status.ValidCatalogSignature,
            "Valid" => Status.NotVerified,
            "NotSigned" or "HashMismatch" or "NotTrusted" => Status.InvalidSignature,
            _ => Status.NotVerified
        };
        return new Result(result, status, signer);
    }
}
