# WUA Type=Software: deduplica UpdateID in tutti i formati GUID (#371)

## Problema

Windows Update e Microsoft Update possono restituire lo stesso UpdateID
GUID rappresentato come D, B, N o P. Il parser Type=Software accettava
tutte queste sintassi ma conservava il testo originale. La selezione
fair tra vendor e il merge dei 32 suggerimenti informativi confrontavano
solo le **stringhe**, quindi più formati dello stesso aggiornamento
occupavano slot distinti e potevano nascondere indizi NVIDIA/Killer tardivi.

## Soluzione

- \`TryCreate\` conserva il valore GUID in formato D canonico.
- \`SelectFair\` canonicalizza anche i record IPC/snapshot legacy prima
  della deduplica e del round-robin per produttore, senza accettare ID non GUID.
- \`Merge\` confronta il valore GUID già presente in destination, input
  e lista omitted (anche se legacy B/N/P), conserva la prima provenienza,
  e conta una sola omissione per un vero UpdateID.
- La matrice delle otto categorie deduplica gli indizi Type=Software
  legacy per valore GUID, senza contaminarli con conteggi Type=Driver.

Questo NON esegue una nuova ricerca WUA, non associa Type=Software a
Hardware ID del PC, non promuove le versioni software a driver installabili
e non modifica \`Aggiorna tutto\`.

Test: D/B/N/P, limiti 32/12.000, WU/MU, input legacy, overflow e
report QA anonimo. Il confronto Driver Easy #91, QA Recoil 16 #136
e firma Authenticode #283 restano aperti.
