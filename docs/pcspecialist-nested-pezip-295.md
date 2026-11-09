# PCSpecialist: ZIP con EXE PE-ZIP annidati (issue #295)

## Caso prima non rilevato
Nell'importazione locale dei driver PCSpecialist, un singolo ZIP può
contenere `GPU/setup.exe` o `Network/driver.exe`. OmegaDrive già
ispezionava in sola lettura un EXE PE con overlay ZIP standard selezionato
direttamente, ma la traversata ZIP -> EXE ignorava completamente tali
contenuti INF. Anche gli EXE proprietari non analizzabili risultavano
invisibili nel riepilogo.

## Comportamento ora
- Con archivio ZIP validato e selezionato dall'utente, il parser
  esamina **al massimo 4 EXE interni** con header PE valido e overlay
  ZIP standard. I PE sono trattati come **dati**: non vengono eseguiti,
  caricati o invocati. Nessun MSI, script o DLL eseguito.
- Il budget unico non supera **8 archivi annidati** (ZIP/CAB/EXE
  selezionati), **1 GiB** di dimensioni dichiarate staged e
  profondità massima **2**; i controlli per ZIP traversal, symlink,
  bombe di decompressione e INF max 4 MiB rimangono attivi.
- Un EXE proprietario senza overlay ZIP, oltre il limite o al terzo
  livello è contato come **non ispezionato**. Il report dichiara
  esplicitamente «copertura parziale», non «nessun driver disponibile».
- Un EXE con header PE realmente malformato, uno ZIP overlay corrotto
  o un percorso ZIP non sicuro interrompono l'intero confronto,
  non producono un falso successo parziale.
- I report dei dispositivi sono uniti tramite indici PnP effimeri,
  senza duplicare la stessa GPU fra INF base e INF nell'EXE.
  L'etichetta visibile è «Archivio nn», senza order ID e nomi
  originali dei file. Nessun invio del numero ordine.

## Limiti e QA
Sono soltanto **evidenze INF**. Non autenticano il catalogo OEM,
la firma CAT, il ranking Windows, l'applicabilità della versione
o la possibilità di installare. Nessun aggiornamento WUA creato,
nessun pacchetto scaricato o installato, `Aggiorna tutto` invariato.
Gli EXE 7z/NSIS/Inno non sono decodificati. QA reale Recoil 16 e
confronto 8-vs-2 ancora aperti in #120/#136/#91; firma #283.
