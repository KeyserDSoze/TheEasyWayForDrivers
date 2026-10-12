namespace TheEasyWayForDrivers.Core.Drivers;

/// <summary>
/// States how a vendor's official source can be accessed, not whether an
/// individual driver has been verified as installable.
/// </summary>
public enum OemCatalogAccess
{
    PublishedCatalog,
    DocumentedLocalTool,
    OfficialSupportOnly
}

public sealed record OemCatalogCapability(
    string Vendor,
    OemCatalogAccess Access,
    string OfficialDocumentation,
    bool RequiresLocalCompanion,
    string Limitations);

public static class OemCatalogRegistry
{
    public static IReadOnlyList<OemCatalogCapability> Sources { get; } =
    [
        new("Dell", OemCatalogAccess.PublishedCatalog,
            "https://www.dell.com/support/kbdoc/en-uk/000122176/driver-pack-catalog", false,
            "Driver Pack per deployment, non singoli driver"),
        new("Lenovo", OemCatalogAccess.DocumentedLocalTool,
            "https://docs.lenovocdrt.com/guides/lcsm/lcsm_top/", true,
            "Lenovo.Client.Scripting richiede MachineType verificato"),
        new("HP", OemCatalogAccess.DocumentedLocalTool,
            "https://developers.hp.com/hp-client-management/doc/get-softpaqlist", true,
            "HP CMSL richiede Platform ID e versione Windows"),
        new("ASUS", OemCatalogAccess.OfficialSupportOnly,
            "https://www.asus.com/support/faq/1050467/", false,
            "MyASUS non equivale a API pubblica"),
        new("Acer", OemCatalogAccess.OfficialSupportOnly,
            "https://www.acer.com/it-it/support/drivers-and-manuals", false,
            "Nessun invio automatico di SNID o seriali"),
        new("MSI", OemCatalogAccess.OfficialSupportOnly,
            "https://it.msi.com/support/technical_details/DT_Driver_Update", false,
            "MSI Center Live Update non espone API pubblica verificata"),
        new("GIGABYTE", OemCatalogAccess.OfficialSupportOnly,
            "https://www.gigabyte.com/Support", false,
            "Catalogo API non verificato"),
        new("Microsoft Surface", OemCatalogAccess.OfficialSupportOnly,
            "https://learn.microsoft.com/en-us/surface/manage-surface-driver-and-firmware-updates", false,
            "MSI per modello e sistema, anche firmware; non esecuzione automatica"),
        new("Intel", OemCatalogAccess.OfficialSupportOnly,
            "https://www.intel.com/content/www/us/en/support/detect.html", false,
            "Intel DSA è companion, non API anonima"),
        new("NVIDIA", OemCatalogAccess.OfficialSupportOnly,
            "https://www.nvidia.com/en-us/software/nvidia-app/", false,
            "NVIDIA App è companion, non API anonima"),
        new("AMD", OemCatalogAccess.OfficialSupportOnly,
            "https://www.amd.com/en/resources/support-articles/faqs/GPU-131.html", false,
            "Auto-Detect non è API anonima"),
        new("Samsung", OemCatalogAccess.OfficialSupportOnly,
            "https://www.samsung.com/support/", false,
            "Samsung Update supportato solo dove applicabile"),
        new("Framework", OemCatalogAccess.OfficialSupportOnly,
            "https://frame.work/support", false,
            "Bundle per modello/generazione, API non verificata"),
        new("Razer", OemCatalogAccess.OfficialSupportOnly,
            "https://mysupport.razer.com/", false,
            "Nessun catalogo API pubblico verificato"),
        new("Logitech", OemCatalogAccess.OfficialSupportOnly,
            "https://support.logi.com/", false,
            "Software companion distinto dai driver PnP")
    ];

    public static OemCatalogCapability? Find(string? vendor) =>
        Sources.FirstOrDefault(source =>
            string.Equals(source.Vendor, vendor?.Trim(),
                StringComparison.OrdinalIgnoreCase));
}
