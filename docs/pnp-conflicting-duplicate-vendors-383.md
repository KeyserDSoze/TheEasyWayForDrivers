# DeviceId duplicati con vendor discordanti: policy coerente fra schermate (#383)

## Contesto
Un inventario Windows può includere due record per la stessa istanza
PnP (DeviceId identico senza distinzione maiuscole/minuscole), anche
con HardwareID incompatibili. L'OEM Companion scartava già la coppia
NVIDIA/Realtek, mentre Home, il selettore locale SetupAPI/PnPUtil, il
confronto delle otto categorie #91 e gli handoff supporto sceglievano
la prima riga con DistinctBy(DeviceId). Di conseguenza, l'ordine dei
record poteva alterare il conteggio e mostrare correlazioni WUA di un
produttore che un'altra parte dell'app reputava non attendibile.

## Regola condivisa
PnpDistinctTrustedDevices.Create raggruppa *prima* per DeviceId,
case-insensitive. Se una riga è internamente "Conflicting" oppure
per una sola istanza compaiono almeno due produttori PnP validati
differenti, non restituisce alcuna riga di quel gruppo. Per i gruppi
senza contraddizioni riconosciute conserva la **prima** riga come
prima: non unisce arbitrariamente HardwareID di sorgenti diverse,
non trasforma un CompatibleID in HardwareID e non accresce le offerte
driver.

Il controllo precede la selezione per nome e handoff OEM, perciò
anche un record contraddittorio con nome vuoto ha effetto. Lo
stesso criterio è ora adottato in Home, lookup Driver Store,
VendorInventoryCoverage, ReferenceDriverCoverage e VendorSourceDiscovery.

## Limiti
È un filtro read-only contro **falsi positivi** di vendor e GUID
associati, non un algoritmo per verificare la compatibilità di
driver OEM. L'assenza di un gruppo scartato non dimostra che il
dispositivo non esista né sia aggiornato. Le prove includono
permutazioni, GPU NVIDIA, codec Realtek, Intel Killer, Qualcomm,
duplicati coerenti, privacy e controlli WUA già confermati.

La misura reale su Windows 11 e Recoil 16 resta #85/#91/#136;
nessuna nuova fonte o installazione è stata aggiunta.
