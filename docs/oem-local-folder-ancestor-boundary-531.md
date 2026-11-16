# OEM INF/CAT audit: blocco dei link in tutti gli antenati (#531)

## Difetto
`WindowsDriverPayloadAuditReader.AuditAsync` controllava la proprietà
`ReparsePoint` soltanto sulla directory immediata dell'INF, a
differenza di `OemFolderCatalogAuditPlan` e
`LocalOemFolderComparison`. Una directory ordinaria sotto una
junction/symlink resta ordinaria, mentre il percorso attraversa un link
in un padre: il controllo Desktop era quindi incompleto.

## Correzione
- Helper condiviso `OemLocalFolderBoundary.RequirePlainLocalDirectory`:
  verifica località, esistenza, attribuiti e assenza di reparse point
  per *ogni* antenato della directory, prima di letture INF/CAT.
- `WindowsDriverPayloadAuditReader` lo chiama prima dell'inventario
  e delle operazioni di hashing/SignTool.
- Test su folder regolare, link diretto, link antenato con figli
  normali e cartella inesistente; errori senza percorso privato.

## Limiti
Questo è un controllo preventivo read-only contro collegamenti già
presenti, **non** una garanzia atomica se altri processi possono
modificare le directory durante l'audit (rischio TOCTOU di #529).
Nessuna firma CAT, membership, compatibilità hardware o autorizzazione
all'installazione si deduce dal percorso locale.
