# Evidenza driver installati: ID WUA incompleti (#535)

## Problema
La Home con le versioni installate (`VendorDriverEvidence`) presentava
`nessun candidato Windows Update correlato per ID` anche quando una
fonte WUA confermata riportava un driver con `AdditionalHardwareIdsIncomplete`
oppure il merge fra fonti aveva scartato ID non validi. L'assenza di un
match **osservato** non prova la completezza della correlazione.

## Correzione
- `VendorDriverEvidenceFinder.Find` utilizza i GUID Type=Driver ufficiali,
  uniti con `WuaDiagnosticAssociations`. Propaga il flag di incompletezza
  calcolato **dopo** il merge ai risultati riconosciuti per vendor.
- Zero match con ID non completi viene espresso come
  `0 correlazioni WUA osservate per ID`, con
  `assenza non verificabile`. Un match esatto resta conteggiato con
  l'avviso `ulteriori associazioni possibili`.
- La ricerca WUA parziale ha un messaggio separato; una ricerca non
  completata ignora i dati stale e non mostra false conferme da COM.
- Test sintetici su NVIDIA GPU, Realtek audio, Killer Wi-Fi, GUID
  multisorgente, fonte non ufficiale, query non verificata e casi
  con liste ID complete.

## Limiti
Questo non certifica un driver disponibile, una versione aggiornata,
compatibilità OEM, firma CAT/INF o applicabilità per-device. Non sono
state aggiunte azioni di download o installazione; resta il collaudo
reale Recoil 16 (#91, #136) e la verifica OEM (#128).
