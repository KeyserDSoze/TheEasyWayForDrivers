using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// #478: Authorization for explicitly requested, read-only Dell deployment
/// and Lenovo scripting catalogs. IPC provider records are untrusted inputs
/// and a vendor name alone does not prove that the provider is applicable.
/// </summary>
public static class AdvancedOemCatalogQueryPolicy
{
    public const string DellUnavailableMessage =
        "Catalogo Dell non verificabile (rete, lettura o timeout). " +
        "Nessun driver installato. Controlla la fonte ufficiale per il PC corrente.";

    public const string LenovoUnavailableMessage =
        "Catalogo Lenovo non verificabile (modulo, rete, autorizzazioni o timeout). " +
        "Non significa zero aggiornamenti. Nessun driver installato.";

    public static bool TryGetDellModel(
        OemProviderStatus? provider, out string model)
    {
        model = string.Empty;
        if (provider is null ||
            !string.Equals(provider.ProviderId, "system-oem", StringComparison.Ordinal) ||
            !provider.IsApplicable ||
            !SystemOemCatalog.IsDellManufacturer(provider.SystemManufacturer))
            return false;

        // The model is used as a Dell catalog lookup key. Do not let
        // unvalidated IPC text (paths, PnP IDs, serials or placeholders)
        // become a query or be reflected into UI results.
        var safeModel = SystemIdentityNormalizer.Clean(provider.SystemModel);
        if (safeModel is null ||
            !string.Equals(provider.SystemModel, safeModel, StringComparison.Ordinal))
            return false;

        model = safeModel;
        return true;
    }

    public static bool CanQueryLenovo(OemProviderStatus? provider) =>
        provider is not null &&
        string.Equals(provider.ProviderId, "system-oem", StringComparison.Ordinal) &&
        provider.IsApplicable &&
        SystemOemCatalog.IsLenovoManufacturer(provider.SystemManufacturer);
}
