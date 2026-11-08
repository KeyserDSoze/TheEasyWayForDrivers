# WUA multi-sorgente nei riepiloghi Home/Vendor — issue #271

## Difetto

Lo stesso GUID WUA Type=Driver può essere restituito da diverse ricerche
complete con Hardware ID / IWindowsDriverUpdateEntry differenti.

La diagnostica aggregata WuaPnpAssociationAudit già univa per GUID.
Le tre card Home (HomeVendorCoverageSummary), gli handoff ufficiali
(VendorSourceDiscovery) e il riepilogo delle versioni installate
(VendorDriverEvidenceFinder) usavano invece DistinctBy(update.Id):
la prima fonte cancellava le evidenze ID della seconda, facendo
apparire 0 correlazioni NVIDIA/Killer/Realtek dove WUA aveva un match.

## Correzione

WuaDiagnosticAssociations riusa WindowsDriverCandidateMerge, già limitato
a 1.024 ID aggiuntivi per GUID e basato esclusivamente su ID WUA letti
e validati. Le tre UI contano ogni GUID solo una volta; lo stesso GUID
può avere una correlazione di sola lettura con più nodi PnP.

La nuova proiezione espone *solo* UpdateId, PrimaryHardwareId e
AdditionalHardwareIds, senza metadati di installazione, tipo di
candidato, controllo del download o pulsanti. La Home continua a
richiedere GUID validi e WUA verificato; hint Type=Software / cache /
pass falliti e cataloghi OEM non entrano nelle nuove associazioni.

L'informazione non determina se un driver è più nuovo, è necessario,
è verificato dal produttore o ha una firma/compatibilità sufficiente.
Non modifica QuickUpdatePolicy, DeviceWuaMatchGroups, la selezione di
installazione o i limiti WUA/IPC. Nessun driver inventato.

## QA

Fixture di due ricerche con lo stesso GUID e ID NVIDIA ACPI, SWC DCH,
Realtek HDAUDIO tardivi; nessun falso match con ID simili; verifica
delle tre superfici, conteggio GUID una volta, budget 1.024 e report
senza Device Instance ID.

Restano da eseguire i test reali sul Recoil 16 (#91 e #136). Questo
fix migliora le associazioni mostrate, **non** introduce nuovi
cataloghi né promette di passare da 2 a 8 driver disponibili.
