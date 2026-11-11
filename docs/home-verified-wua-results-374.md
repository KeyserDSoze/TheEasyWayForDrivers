# Home / elenco WUA: stesso conteggio GUID verificato (#374)

Home e la lista aggiornamenti devono rappresentare **le stesse offerte
effettivamente confermate**. Dopo #373 la lista principale controlla la
provenienza per sorgente WUA, ma SimpleScanPresentation ancora contava
le righe IPC grezze, perfino da pass falliti o con duplicati di GUID.
Questo produceva una headline "N possibili aggiornamenti" disallineata
dall'elenco e dalla notifica.

La nuova funzione read-only `ConfirmedDistinctForReport` filtra prima
per fonte WUA Type=Driver riconosciuta **con proprio pass Completed**,
poi unisce i record per GUID reale normalizzato D (forme D/B/N/P).
Nessun pass fallito può contribuire altri PnP ID, metadati optional o
firmware a un GUID di pass riuscito. UI e Home chiamano lo stesso metodo.

Il messaggio a elenco vuoto richiede un pass Type=Driver completato
dell'elenco ufficiale: uno stato Completed di una fonte inventata o
Type=Software non può diventare "nessun aggiornamento proposto".
I risultati parziali mantengono l'avviso, i pacchetti OEM assessed sono
separati dai candidati WUA e gli handoff non diventano installabili.

Non cambia la query WUA, la selezione dei driver installabili,
l'install-time recheck del servizio o la raccolta OEM. Non prova che un
driver NVIDIA/Killer sia disponibile sul Recoil 16 (#91/#136).
La QA visuale 100/125/150% DPI (#114) resta aperta.
