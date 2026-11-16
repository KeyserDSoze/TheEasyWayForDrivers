# WUA Partial: contare tutte le voci COM senza confonderle con i titoli (#515)

## Difetto
`SearchPass` legge `result.Updates.Count`, enumera tutte le voci
in un risultato WUA parziale e conserva soltanto titoli legibili e
sicuri come indizi di revisione. `TrySearchPass` però scriveva
`Candidates = partialStaged.Count`: 4 voci COM con un titolo illeggibile
diventavano 3 «risultati» nel resoconto della fonte. La regressione
era resa più evidente dal recupero dei titoli facoltativi (#514).

## Cambiamento
- Nel ramo `Partial` il provider conserva `enumeration.Count` solo
  **dopo** avere enumerato con successo l'intera collezione WUA.
- `WuaPartialPassAccounting.Record` accetta i conteggi grezzi e
  dei soli titoli validati, non confonde deduplicazione e voci lette.
- `Candidates` indica le voci **grezze** WUA di quella ricerca
  incompleta; `NewUniqueCandidates` resta **zero** perché non ci
  sono GUID candidati convalidati per l'installazione.
- `Detail` riporta anonimamente voci COM, titoli sicuri, voci senza
  titolo leggibile/ID valido e nuovi indizi diagnostici;
  non copia titoli, GUID, URL di policy o messaggi COM.
- Contatori validi `0 <= newHints <= safeTitles <= raw <= 4000`;
  chiamate con outcome diverso da Partial, contatori inventati,
  negativi o oltre limite sono rifiutate.
- Errori strutturali in `Item/Identity/UpdateID`, cancellazione
  e failure COM non arrivano alla contabilizzazione, e un pass
  `Partial` non alimenta `Aggiorna tutto`.

## Test
`WuaPartialPassAccountingTests` e integrazione con i fake COM di
`WuaPartialTitleGetterIsolationTests` coprono WUA 4→3,
duplicati, titolo errato, conteggi 0/1/4000, privacy e nessun
candidato installabile.

Il risultato incompleto WUA è un indizio, non una prova che ci siano
driver mancanti. Restano #92 (QA WSUS/offline), #136/#91 (Recoil 16)
e #85 (Windows end-to-end).
