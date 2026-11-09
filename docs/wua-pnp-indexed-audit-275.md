# WUA → PnP: indicizzazione del report diagnostico (#275)

## Problema
La scansione Home esporta un report anonimo per capire perché una
fonte WUA non ha attribuito i suoi GUID ai device locali. La diagnosi
`WuaPnpAssociationAudit` calcolava ogni coppia Device × Update e
bloccava il report oltre 50.000 coppie: **500 device e 101 offerte**,
pur essendo una combinazione legittima, venivano descritti come
`associazioni non valutabili`. Nessun dettaglio sui GUID attribuiti
a NVIDIA/Killer/Realtek era più misurabile.

## Modifica
- Gli Hardware ID esatti e Compatible ID vengono indicizzati una
  volta; per ogni GUID WUA si effettuano lookup per ID dichiarato.
- Precedenza invariata: esatto, compatibile, modello pubblico generico
  (sempre *review-only*), non attribuito.
- I soli modelli deboli da Hardware ID specifici vengono indicizzati
  tramite `WuaGenericModelCorrelation.PublicReviewModelKeys`; la verifica
  finale usa **lo stesso predicato fail-closed**, senza inferenza dal
  nome del dispositivo o dall'Hardware ID di un altro vendor.
- Restano merge di GUID da fonti WUA completate, 2048 device,
  4000 record WUA, cap 1024 ID per aggiornamento. In sostituzione
  del prodotto artificiale 50k si limita il lavoro effettivo a
  262.144 identificativi PnP e 2.000.000 identificativi WUA
  grezzi, altrimenti il report indica esplicitamente `OverBudget`.

## Sicurezza e test
Il report mostra solamente **contatori** e categorie fisse;
mai Device Instance ID, Hardware ID, GUID WUA, titoli di update
o informazioni OEM identificanti. Non cambia ricerca, download,
policy `QuickUpdatePolicy`, Aggiorna tutto o installabilità.

Test: 500×101 GUID non più bloccati; 500×180 con corrispondenza
esatta NVIDIA, compatible Killer, modello debole Realtek;
contatori negativi e fail-closed su grandi array PnP;
nessuna regressione dei report aggregati esistenti.

Questo aumenta la possibilità di verificare il gap 2 contro 8
sul Recoil 16 (#91/#136), **non aumenta la disponibilità dei driver**.
