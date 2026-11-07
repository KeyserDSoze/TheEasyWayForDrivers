# OmegaDrive — archivio ZIP OEM oltre 1 GiB, solo lettura (#222)

## Problema nella v0.0.105

L'ispezione dei pacchetti driver con ZIP superiore a 1 GiB veniva interrotta
prima di leggere qualsiasi INF, anche quando i file INF erano minuscoli e
non serviva decomprimere il payload NVIDIA/Intel/Realtek. Lo stesso accadeva
quando un singolo elemento non-INF superava 512 MiB o gli elementi dichiarati
superavano 2 GiB. Questo è un limite di **copertura del catalogo locale**,
non una prova che sul PC manchino nuovi aggiornamenti installabili.

## Nuovi limiti selettivi

| Origine | Dimensione del file selezionato | Elemento dichiarato | Totale dichiarato |
| --- | --- | --- | --- |
| ZIP esterno, contenuto verificato come ZIP | **4 GiB** | **4 GiB** | **8 GiB** |
| CAB e EXE-PE con overlay ZIP | invariato **1 GiB** | limite CAB invariato | limite CAB invariato |
| ZIP/CAB annidati copiati nel temporaneo | invariato **512 MiB per figlio** | archivio interno ricontrollato | invariato **1 GiB per pacchetto** |

- Lo ZIP grande viene **indicizzato** tramite central directory: OmegaDrive
  non estrae i file SYS, DLL, EXE e non avvia installer.
- Gli INF rimangono limitati a **96 file, 4 MiB per INF e 24 MiB totali**,
  anche negli ZIP grandi. Un archivio che eccede quei limiti **fallisce**,
  non diventa «nessun driver».
- Restano attivi controlli di path traversal, link simbolici, nomi duplicati,
  rapporto compressione anomalo, massimo 2048 entry, limiti sulle sezioni INF
  e verifica per hardware ID esatto o compatibile.
- Non cambia il tetto di **4 GiB per batch** di archivi selezionati, con massimo
  12 pacchetti. Un ZIP da quasi 4 GiB non può essere sommato ad altri pacchetti
  se supera il limite cumulativo.
- Un file `.zip` con contenuto CAB/EXE o qualsiasi altro formato non ZIP
  **non** può sfruttare il limite allargato: l'ispezione controlla la firma
  reale prima di applicare le regole ZIP.
- Un altro ZIP a profondità superiore al secondo livello continua a essere
  non analizzato ed è dichiarato esplicitamente come copertura parziale.

## Come provare

In **Dettagli avanzati**, dopo **Analizza il PC**, scegliere il confronto
di un archivio locale ZIP OEM ufficiale scaricato dall'utente, oppure il
confronto multiplo. Gli INF trovati vengono correlati all'inventario PnP,
non promossi ad aggiornamenti WUA.

I test automatizzati usano uno stream **sparso virtuale** lungo oltre 1 GiB,
con directory ZIP leggibile alla fine e un INF minuscolo all'inizio: così
possiamo provare lo spostamento del limite senza creare file da GiB in CI.
Test negativi provano che contenuti non-ZIP oltre 1 GiB e stream oltre
4 GiB restano rifiutati.

**Nessuna promessa di installabilità**: versione numerica superiore e
Hardware ID non bastano a verificare autenticità CAT, integrità payload,
ranking SetupAPI, appropriato canale OEM, modello/OS e firma. Nessuna
installazione automatica OEM, nessuna modifica di `DriverUpdateInfo`
o `Aggiorna tutto`.

## QA hardware ancora richiesto

Confrontare pacchetti ufficiali NVIDIA/Intel/Killer del PCSpecialist Recoil 16
in ambiente Windows reale, riportando solo conteggi anonimi e stati fonte.
Il divario con Driver Easy resta aperto in #91 e #136. La CI sintetica
non attesta nuove versioni effettivamente applicabili.
