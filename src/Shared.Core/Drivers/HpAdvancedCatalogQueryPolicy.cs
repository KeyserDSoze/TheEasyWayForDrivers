using TheEasyWayForDrivers.Core.Models;

namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// The advanced HP CMSL button is a read-only, user-confirmed query.
/// The selected provider is an IPC record, not proof of the real OEM.
/// </summary>
public static class HpAdvancedCatalogQueryPolicy
{
    // Displayed on failure/timeout instead of arbitrary PowerShell output:
    // exception messages may include local profiles, paths or serials.
    public const string UnavailableMessage =
        "HP CMSL non disponibile o interrogazione non completata " +
        "(modulo, rete, autorizzazioni o timeout). " +
        "Nessuna conclusione sugli aggiornamenti disponibili; nessun driver installato.";

    public static bool CanQuery(OemProviderStatus? provider) =>
        provider is not null &&
        string.Equals(provider.ProviderId, "system-oem",
            StringComparison.Ordinal) &&
        provider.IsApplicable &&
        SystemOemCatalog.IsHpManufacturer(provider.SystemManufacturer);
}
