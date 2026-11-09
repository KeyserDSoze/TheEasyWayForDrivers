# OEM Hardware ID evidence (#74)

Il confronto read-only usa gli ID dichiarati dagli INF analizzati in memoria nello ZIP locale (#72) e gli Hardware ID / Compatible ID presenti nell'inventario corrente. Usa **solo uguaglianza completa senza distinzione maiuscole/minuscole**, mai somiglianze di nome commerciale o substring.

Il report mostra prima gli Hardware ID esatti, poi i Compatible ID; include dispositivo, percorso INF e identificativo dichiarato. Fino a 40 corrispondenze sono mostrate in Discovery+. Nessun match significa soltanto che non è stata trovata una corrispondenza nell'evidenza analizzata.

**Non si tratta di una conferma di compatibilità**: nessuna valutazione del ranking SetupAPI, versione Windows, architettura o firma CAT. Nessun download/installazione. Il modulo Core resta limitato a 2048 dispositivi e 500 potenziali match e fallisce se supera questi limiti. Sono necessari QA Windows reale (#5) e verifiche #46/#58.

## Allineamento anteprima ZIP / confronto mirato (#314, v0.0.174)

La semplice uguaglianza del testo **non è sufficiente per PCI**.
`PCI\\CC_030000`, `PCI\\CC_0280`, `PCI\\VEN_10DE` e
`PCI\\VEN_10DE&CC_0300` descrivono classe o vendor, non un modello
`VEN+DEV` individuale. L'Inspector li mantiene nell'anteprima INF,
ma **non li mostra come Hardware ID esatti o Compatible ID del PC**,
neppure se Windows dichiara lo stesso token come HardwareID. La
validazione è applicata a entrambi i lati, dispositivi Windows e
dichiarazioni INF, anche per snapshot legacy o sintetici.

Gli ID completi e validi di NVIDIA, Intel Killer e Realtek continuano
a confrontarsi per uguaglianza. Il primo ID della riga Models è
l'Hardware ID primario, gli alias successivi restano compatibili e
non acquisiscono priorità esatta.

Se una preview INF si ferma ai primi 500 ID, è **non esaustiva**:
per cercare i dispositivi reali usare `OemZipInfEvidence.Inspect(zip,
targetDevices)`, che analizza l'intero INF entro budget e non converte
ID generici in match. Gli INF rifiutati o non letti rimangono un gap
di copertura, non una prova di assenza di driver. Lo ZIP Inspector
non valuta firma, ranking Windows o installabilità.
