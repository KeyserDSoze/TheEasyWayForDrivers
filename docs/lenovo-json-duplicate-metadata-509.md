# Metadati Lenovo: chiavi JSON contraddittorie (#509)

## Diagnosi
I parser `LenovoApplicableUpdateParser` (`Get-LnvUpdate`, current PC)
e `LenovoCatalogParser` (`Find-LnvUpdate`, catalogo) usavano
`JsonElement.TryGetProperty`. Un oggetto con doppie chiavi JSON,
per esempio `Type=BIOS` seguito da `Type=Driver` oppure
`IsInstalled=true` seguito da `IsInstalled=false`, poteva
far apparire un pacchetto OEM contraddittorio come driver necessario.
Nel catalogo `PackageType=4` e `PackageType=2` comportavano
analoga ambiguita.

## Correzione
- Helper `OemJsonMetadataIntegrity.HasDuplicateCriticalKeys`: rileva
  chiavi ripetute in modo case-insensitive **prima** del filtro driver.
- LCU: controlla `Type`, `Category`, `IsApplicable`,
  `IsInstalled`, `PackageID`, `Title`, `Version`,
  `ReleaseDate`, `Severity`.
- LCSM: controlla `PackageType`, `ID`, `Name`, `Version`,
  `Category`, `ReleaseDate`.
- Un record ambiguo non partecipa a deduplicazione o display;
  i record indipendenti validi restano visibili e la fairness per
  Graphics/Network/Audio e preservata.
- Restano i limiti sulla risposta **grezza** (1000 record) prima di
  validazioni o dedup, e la sanitizzazione di campi OEM privati.
- Proprietà sconosciute duplicate restano ignorate.

## Test
`LenovoDuplicateJsonMetadataTests` include tipi BIOS/driver
contraddittori, flag corrente-PC, identita duplicate, casing diverso,
categorie minoritarie, quota 1000 e privacy.

I risultati Lenovo rimangono metadati o consigli dell'utility del
produttore: nessun download o installazione OmegaDrive e autorizzati.
QA reale Lenovo #102/#14, Windows #85/#136 e firma #283
rimangono aperti.
