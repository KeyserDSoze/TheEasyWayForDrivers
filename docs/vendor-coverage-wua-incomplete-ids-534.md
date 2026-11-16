# Copertura per produttore e ID WUA incompleti (#534)

## Problema
La diagnostica della Home e `Copia copertura` presentavano zero
correlazioni NVIDIA/Intel/Realtek/Killer per un GUID Windows Update
anche se il relativo `AdditionalHardwareIdsIncomplete` indicava
che non tutti gli Hardware/Compatible ID della voce Type=Driver
erano stati esaminati. Questo non dimostra l'assenza di un pacchetto
driver; è una lacuna nell'enumerazione delle **associazioni PnP**.

## Modifica
- `VendorInventoryCoverage.Create` usa le associazioni GUID WUA
  ufficiali e validate, dopo il merge fra fonti, per rilevare eventuali
  ID PnP incompleti; record OEM/sorgenti sconosciute non possono
  impostare il flag. Le quote di sicurezza continuano a prevalere.
- `VendorCoverageRow.DisplayLine` e `Report.ScopeNote` aggiungono
  un avviso quando zero associazioni sono solo **zero osservate**;
  preservano match già verificati e l'avviso sulle fonti WUA parziali.
- `ScanCoverageText` allega lo stesso caveat al report **anonimo**
  esportabile. Nessun nome, GUID, percorso o Hardware ID viene esportato.
- Nuovi test con NVIDIA GPU e Realtek audio, GUID multisorgente,
  fonte esterna ignorata, limite diagnostico e stato della fonte.

## Sicurezza e limiti
Non si crea alcun aggiornamento, non si risolve un match per analogia
di titolo/vendor e non si abilita `Aggiorna tutto` per gli OEM. Una
voce Type=Driver con ID incompleti potrebbe non avere **alcun**
altro device corrispondente sul PC. La reale disponibilità di driver
RTX 4060/Killer/Audio richiede verifica su hardware Recoil 16 (#91,
#136) e firma/ranking OEM (issue #128, #130).
