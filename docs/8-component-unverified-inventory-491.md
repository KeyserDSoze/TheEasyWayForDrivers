# Matrice 8 componenti e inventario PnP non verificato (#491)

## Difetto
Una query WUA Type=Driver conclusa può riportare un GUID valido mentre
l'inventario PnP Windows non è disponibile o ha fallito. La Home e il
report QA Intel/Killer/NVIDIA mostravano "0 candidati correlati per ID"
partendo dal valore di default, non da una correlazione reale.

## Correzione
- Se l'inventario PnP non è verificato, le righe hardware segnalano
  "associazioni WUA Type=Driver per ID non valutabili".
- Una ricerca WUA fallita rimane "ricerca non verificata"; una completata
  rimane una fonte interrogata ma senza prova di matching hardware.
- Le fonti WUA parziali sono distinte dai problemi d'inventario.
- I titoli WUA Type=Software restano solo informativi e indipendenti.
- Con inventario verificato restano i conteggi esatti, per GUID e per ID.

## Limiti
Non cambia il numero di offerte WUA, il matching PnP/INF/SetupAPI, le
autorizzazioni di installazione né le fonti OEM. Il report è anonimo:
nessun nome dispositivo, Hardware ID, GUID o percorso privato copiato.
QA hardware reale Recoil 16 ancora necessario (#91/#111/#136/#85).
