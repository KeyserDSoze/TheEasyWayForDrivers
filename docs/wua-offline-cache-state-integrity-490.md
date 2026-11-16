# Integrità stati cache WUA per ricerche offline (#490)

## Diagnosi
`MergeBalanced` deduceva l'ambiguità installato/non installato da qualsiasi
GUID parseable nei due elenchi, anche se `TryCreate` avrebbe scartato
il titolo del record. Un record legacy con `WasInstalled` invertito poteva
inoltre prevalere sullo stato effettivo della query che lo aveva prodotto.

## Modifica
- Validazione completa (`TryCreate`) di ogni voce della query offline
  `IsInstalled=0` e `IsInstalled=1` prima di calcolare i GUID condivisi.
- Stato `WasInstalled` assegnato dalla **query di provenienza**, non dal
  flag presente nel singolo record IPC; file e identità PnP invariati.
- Solo GUID con due titoli validi nelle due fonti diventano stato ignoto.
- Null, GUID malformati o titoli non sicuri non consumano i 32 slot.
- I limiti di 4000 righe grezze per fonte rimangono applicati prima del filtro;
  l'interleaving conserva la quota di entrambi i pass e le omissioni distinte.

## Garanzie e verifiche
Queste sono informazioni **storiche WUA**, mai offerte online correnti;
`DriverUpdateInfo` e `Update All` non sono modificati. Suite regressione
su collisioni privacy, stati invertiti, input null, fairness, GUID equivalenti
e ambiguità reale fra le due query. QA hardware Recoil 16, firma e cataloghi
OEM ancora da verificare indipendentemente (#85/#136/#91/#283).
