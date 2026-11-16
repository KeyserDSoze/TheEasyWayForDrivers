# PCSpecialist: controllo degli antenati della cartella OEM (#538)

## Problema
Il planner `PcSpecialistLocalCatalogPlan.Create` rifiutava i
reparse point soltanto sulla root scelta e negli elementi raggiunti
dal DFS. Una root apparentemente normale sotto `parent/junction/`
superava quindi il controllo iniziale e poteva enumerare archivi
provenienti da un'altra cartella o da un mount non verificato.

## Correzione
- Il preflight usa `OemLocalFolderBoundary.RequirePlainLocalDirectory`
  prima di qualsiasi enumerazione del catalogo; rifiuta percorsi
  UNC/device e symlink/junction in **tutti** gli antenati.
- Conserva la lettura locale in sola modalità di inventario e tutti i
  limiti già presenti: 3.000 file, 24 sorgenti, 24 probe EXE, quote ZIP/CAB.
- Test con directory reale contenente INF+ZIP, alias alla root,
  junction/symlink in un genitore con child ordinario, path mancante
  e UNC, mantenendo i dettagli del path fuori dalle eccezioni.

## Limiti
Il controllo degli antenati è preventivo e **non** atomico contro
la sostituzione concorrente di directory già verificate (TOCTOU).
Nessuna firma CAT, origine PCSpecialist, ranking SetupAPI, versione
necessaria o applicabilità al singolo device è certificata.
Confronto su hardware reale Recoil 16 resta #120/#136; il rischio
TOCTOU richiede ulteriori mitigazioni, analoghe a #529.
