# SetupAPI: contare i nodi driver il cui rank è illeggibile (#513)

## Perché
La diagnostica Windows locale enumerava nodi tramite
`SetupDiEnumDriverInfo` e leggeva il rank con
`SetupDiGetDriverInstallParams`. In caso di errore su quest'ultimo,
il nodo veniva scartato dalla lista, e il report mostrava come
«driver enumerati» **solo** quelli con rank leggibile.
Quando nessun rank era recuperabile, non veniva nemmeno detto
che Windows aveva trovato dei nodi compatibili da esaminare.
Questo non significa che siano nuove offerte online o driver da
installare.

## Cambiamento
- Il reader nativo conta `unreadableRanks` per ciascun nodo
  `SetupDiEnumDriverInfo` riuscito con lettura rank fallita.
- Il riepilogo con parametro opzionale `driversWithoutRank` mostra:
  **nodi locali enumerati**, **rank leggibili**,
  **rank non letti** e **righe effettivamente mostrate**.
- Un numero positivo di rank non letti rende il report **PARZIALE**
  anche quando la terminazione dell'enumerazione è normale.
- Se **tutti** i rank sono illeggibili, `State.NotVerified`
  con un conteggio aggregato; zero nuovi driver/installabili non è
  una conclusione lecita.
- I 256 slot comprendono anche i nodi con rank non letto,
  senza troncamento silenzioso; input malformati negativi o sopra
  budget sono rifiutati.
- Le righe con rank valido conservano precedenza
  `Rank` numerico, poi data INF e versione. Rank mancante
  **non è rank zero**.
- Nessun dato Instance ID, HW ID, nome PC o percorso viene
  aggiunto all'output: solo contatori anonimi e label già
  sanitizzate. Nessuna installazione o staging.

## Test
`WindowsSetupApiUnreadableRankAccountingTests` copre
all-rank-failure, failure parziale di alcuni nodi,
enumerazione completa, budget 256, errori aggregati,
non installabilità e privacy.

## Limiti aperti
Questo è ranking SetupAPI di driver **già locali** e per
dispositivo selezionato, non una classificazione di pacchetti
OEM non staged. Mancano QA reale Recoil 16 e verifica end-to-end
della firma/rank con pacchetti ufficiali (#130/#132/#136).
