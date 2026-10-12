# Discovery+ — risultati per singolo dispositivo (incremento #38)

La vista **Discovery+** ora prevede due modalità:
- **Tutte le fonti**: mostra risultati WUA cercati e metadati OEM che l'utente ha consultato durante la sessione corrente.
- **Solo candidati Windows Update del dispositivo selezionato**: selezionare prima un dispositivo nella scheda *Driver installati*, poi abilitare il filtro in Discovery+. Rimangono soltanto candidati WUA il cui Hardware ID coincide esattamente con un Hardware ID o Compatible ID dell'inventario del dispositivo.

La nuova classe `DiscoveryDeviceEvidence` restituisce un'evidenza numerica: **2** = Hardware ID esatto, **1** = Compatible ID, **0** = nessuna associazione verificata. Un identificativo corrispondente non certifica l'installazione sicura.

**Dell Driver Pack e HP SoftPaq non vengono assegnati automaticamente al singolo dispositivo**, anche se nel catalogo compaiono nomi somiglianti: servirebbe prova di compatibilità tramite ID hardware/INF e OS, non ancora presente nei relativi metadati normalizzati. Lenovo rimane consultabile nella scheda OEM in attesa di dati e schema reale verificati.

Una **nuova scansione hardware cancella i risultati WUA e OEM mantenuti in memoria**, perché rappresentano la ricerca associata all'inventario precedente. I pacchetti OEM non vengono scaricati né installati automaticamente. Tutto il filtro è locale.

Questa fase non modifica il servizio privilegiato o il flusso d'installazione. Le prove runtime su PC Windows 11 sono tracciate in #5. Refs #38, #34, #36.
