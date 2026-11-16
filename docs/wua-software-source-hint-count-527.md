# WUA Type=Software: provenienza e conteggi per fonte (#527)

## Difetto
La lista review `Type=Software` (NON driver installabili) veniva filtrata
sulla presenza di almeno un pass `Completed`, senza riconciliare quanti
titoli IPC fossero attribuiti a ciascun pass. Era quindi possibile mostrare
un titolo di review per una fonte WUA che aveva restituito zero risultati
COM. Inoltre, duplicati dello stesso nome fonte con casing differente non
invalidavano sempre lo status `Completed` canonico.

## Correzione
- `ConfirmedForReport` conserva solo indizi attribuiti a un pass
  `Completed` canonico e unico, se il numero totale di titoli attribuiti
  a QUELLA fonte non supera il suo `Candidates` (conteggio grezzo COM).
  Una fonte inconsistente non impedisce di visualizzare gli indizi di
  una seconda fonte indipendente coerente.
- I pass software sono confrontati case-insensitive **per individuare
  duplicati**; la verifica della fonte valida rimane case-sensitive.
- La selezione bilanciata fino a 32 titoli opera soltanto DOPO il filtro
  per provenienza. Non si convertono hint in `DriverUpdateInfo`.

## Prove e limiti
`WuaSoftwareSourceHintCountTests` verifica zero/uno, overcount, fonte
indipendente, alias di casing, e casi validi. Test sintetici IPC/COM,
non una ricerca su macchina Windows reale. I rapporti Type=Software
restano diagnostici, non avviano download/installazione driver e non
colmano da soli la differenza di copertura Driver Easy #91/#136.
