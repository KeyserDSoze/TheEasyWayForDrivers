# OEM Hardware ID evidence (#74)

Il confronto read-only usa gli ID dichiarati dagli INF analizzati in memoria nello ZIP locale (#72) e gli Hardware ID / Compatible ID presenti nell'inventario corrente. Usa **solo uguaglianza completa senza distinzione maiuscole/minuscole**, mai somiglianze di nome commerciale o substring.

Il report mostra prima gli Hardware ID esatti, poi i Compatible ID; include dispositivo, percorso INF e identificativo dichiarato. Fino a 40 corrispondenze sono mostrate in Discovery+. Nessun match significa soltanto che non è stata trovata una corrispondenza nell'evidenza analizzata.

**Non si tratta di una conferma di compatibilità**: nessuna valutazione del ranking SetupAPI, versione Windows, architettura o firma CAT. Nessun download/installazione. Il modulo Core resta limitato a 2048 dispositivi e 500 potenziali match e fallisce se supera questi limiti. Sono necessari QA Windows reale (#5) e verifiche #46/#58.
