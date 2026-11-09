# NVIDIA ACPI con revisione: correlazione WUA solo informativa (#288)

## Difetto
I componenti Platform NVIDIA possono avere un Hardware ID del tipo
`ACPI\\NVDA0820&REV_01` o `ACPI\\NVDA0820&REV_0001`, mentre
un'offerta WUA Type=Driver, se presente, potrebbe elencare solo
`ACPI\\NVDA0820`. Prima il modello pubblico non era ottenibile da
questi ID con REV e `WuaGenericModelCorrelation` escludeva ogni ACPI.
Il GUID appariva non attribuito nella diagnostica, se il PnP non aveva
già un Compatible ID esatto.

## Correzione
- Parser `MicrosoftUpdateCatalogSearch`: **solo vendor NVIDIA
  `ACPI\\NVDA` + quattro cifre hex**, con suffisso `&REV_` opzionale
  esattamente 2/4 cifre esadecimali. Nessun UID, SUBSYS, seriale,
  percorso istanza, suffisso arbitrario o vendor ACPI sconosciuto.
- `PublicReviewModelKeys` ammette soltanto la stessa famiglia ACPI
  NVDA dopo validazione e rimozione REV, **solo da HardwareIds**.
- `DeviceWuaMatchGroups` condivide il filtro indicizzato; l'offerta
  WUA generica entra esclusivamente in `ModelOnlyReview`, mai
  `Ordinary` o `ReviewOnly` che presumono ID corrispondenti.
- Se Windows espone il modello come vero Compatible ID, l'associazione
  esatta **resta** quella ordinaria soggetta alle normali regole WUA.
- Il report aggregato mostra il numero di indizi senza esportare
  Hardware ID, REV, Device Instance ID o dati di modello del PC.
- Un WUA sconosciuto, firmware/BIOS o modello diverso non viene
  associato come indizio.

## Limiti
Si recupera un riferimento WUA esistente, non un GUID aggiuntivo o un
download NVIDIA. Un indizio sul modello ACPI non convalida
versione, compatibilità OEM, firma CAT o possibilità di installare.
Il portale Microsoft Update Catalog si apre solo manualmente:
nessun accesso automatico. `QuickUpdatePolicy` e `Aggiorna tutto`
rimangono invariati.

Riferimento Microsoft ACPI:
https://learn.microsoft.com/en-us/windows-hardware/drivers/bringup/device-management-namespace-objects

QA su Recoil 16 reale e verifica 8-vs-2 rimangono #99/#91/#136.
