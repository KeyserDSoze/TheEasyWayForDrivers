# Correlazione per dispositivo con indice WUA (issue #281)

## Collo di bottiglia
Dopo il merge per GUID di #278, la Home richiama `Prepared.ForDevice` per
ogni dispositivo. In precedenza `FindMatches` scandiva tutti gli update
e fino a 1024 ID aggiuntivi per ciascuno; anche gli indizi di modello
pubblico ripetevano la scansione completa. Un inventario di 500 dispositivi
e 200 offerte imponeva un costo proporzionale a dispositivi × offerte × ID.

## Modifica
`DeviceWuaMatchGroups.Prepared` crea una sola volta un indice in memoria
case-insensitive `ID hardware WUA → GUID`, dai soli campi
`HardwareId/AdditionalHardwareIds` già validati e normalizzati dal merge
`WindowsDriverCandidateMerge`. I device interrogano l'indice con i loro
Hardware/Compatible ID. Ogni GUID viene deduplicato; il matcher originale
determina ancora la priorità Hardware ID > Compatible ID e l'ordinamento.

Gli indizi di modello generico usano soltanto le chiavi pubbliche prodotte
da `PublicReviewModelKeys` per restringere i candidati e passano ancora da
`IsWeakModelOnlyMatch`, che esclude fonti sconosciute, firmware, metadati
incompleti e associazioni per titolo. Restano esclusivamente
`ModelOnlyReview`: mai `Ordinary` e mai installazioni rapide.

Ogni update indicizza al massimo 1 ID principale + 1024 ID aggiuntivi.
Per evitare memoria non limitata con payload IPC anomali, dopo 250.000
associazioni viene usato il vecchio percorso completo e conservativo,
**senza scartare offerte o creare falsi zero**.

## Regressioni / sicurezza
Fixture: 500 device e 200 offerte con ID secondari multipli, NVIDIA, Killer,
Realtek, Bluetooth, ID equivalenti con case/spazi, ID near-miss, precedenza
Hardware/Compatible, advanced review-only, titoli OEM non associati e
limite 1024. Nessuna modifica al gate `QuickUpdatePolicy` o al recheck del
servizio. Nessun nuovo catalogo, download o installazione OEM.

L'indice accelera associazioni delle offerte WUA già trovate; non implica
un aumento dei driver installabili. Il confronto su Recoil 16 resta in #136.
