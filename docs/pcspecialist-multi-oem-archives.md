# OmegaDrive — confronto batch dei pacchetti OEM ZIP/CAB (#204)

## Caso PCSpecialist / Recoil 16

Il [portale ufficiale PCSpecialist](https://www.pcspecialist.co.uk/download-drivers/)
consente di reperire i pacchetti relativi al computer tramite
numero d'ordine. **Il numero va immesso solo nel browser sul sito
PCSpecialist.** OmegaDrive non lo legge, richiede né invia. NVIDIA
indica il produttore del notebook come fonte primaria per driver
qualificati per le personalizzazioni del dispositivo.

La v0.0.97 consentiva di confrontare **un archivio alla volta**:
selezionare un secondo ZIP/CAB rimuoveva le evidenze del primo. Un
pacchetto GPU NVIDIA, uno audio NVIDIA e uno Killer potevano essere
verificati soltanto con operazioni separate.

## Nuovo flusso

1. Eseguire **Analizza il PC** su Windows per ottenere un inventario
   PnP completo.
2. Nel sito ufficiale PCSpecialist scegliere il proprio sistema
   operativo e scaricare localmente i driver pertinenti
   (senza inserire dati d'ordine in OmegaDrive).
3. Aprire **Dettagli avanzati → Confronta ZIP/CAB multipli**;
   selezionare da uno a **12** ZIP/CAB locali (Ctrl/Shift).
4. OmegaDrive controlla la lista selezionata **prima** di iniziare:
   archivi ordinari (non symlink), estensioni esatte ZIP/CAB,
   nessun percorso duplicato, massimo **1 GiB per archivio** e
   **4 GiB complessivi**. La lettura totale ha timeout di 5 minuti.
5. Ogni archivio passa attraverso la pipeline preesistente di
   preflight INF, hardware ID/Compatible ID, target Windows
   e versione. Per CAB viene usato soltanto il `expand.exe`
   ufficiale di Windows, con INF temporanei; non viene
   estratto/eseguito nessun installer, EXE o MSI OEM.
6. Le evidenze di ogni archivio usano un **indice numerico
   temporaneo** dell'inventario PnP locale: gli Instance ID,
   seriali e Hardware ID non vengono trasferiti nel report.
   L'aggregazione distingue realmente i dispositivi **distinti**
   dalle associazioni ripetute negli ZIP/CAB, anche quando
   due dispositivi hanno lo stesso nome generico Windows.
7. Per ogni dispositivo viene mostrata **una sola riga**, scelta
   fra le evidenze disponibili: priorità a compatibilità Windows
   e Hardware ID esatto, poi versione a quattro segmenti.
   Fino a **80 dispositivi** distribuiti equamente tra i pacchetti;
   `Archivio 02 · presente in 3 archivi` indica una fonte anonima,
   non un percorso o nome file originale. Per ogni archivio
   il limite originale di 80 righe resta invariato.
8. Il riepilogo distingue **numero esatto di dispositivi
   distinti correlati** (inclusi quelli oltre le 80 righe),
   associazioni totali ai pacchetti, associazioni ripetute,
   righe uniche non mostrate, INF scartati e versioni superiori
   **nelle sole righe visibili**. La deduplica non implica
   che il driver sia firmato o pronto da installare.

La selezione di **un solo archivio** continua a usare il
confronto singolo della release precedente. La verifica
firme INF/CAT e dei file SYS/DLL estratti resta una funzione
separata, attivabile soltanto su cartelle locali già estratte.

## Sicurezza / correttezza

- **All or nothing**: se qualunque ZIP/CAB selezionato non
  supera un controllo, non vengono pubblicati risultati
  parziali come se tutti i pacchetti fossero stati analizzati.
- L'analisi non scarica driver, non esegue installatori,
  non richiede credenziali/seriali e non comunica con
  PCSpecialist o NVIDIA. I nomi degli archivi, i percorsi
  locali e gli originali nomi INF non entrano nell'aggregato.
- I confronti usano il preflight dell'INF, non il ranking
  SetupAPI effettivo del pacchetto esterno. Una versione
  maggiore e un Hardware ID esatto NON provano firma,
  integrità, migliore driver scelto da Windows né
  applicabilità definitiva.
- `IsInstallable=false` e `IsVerifiedCompatible=false`
  per ogni report batch. Nessun risultato locale va alla
  lista WUA, `Update` o `Update All`. Firmware e BIOS
  non diventano driver approvati. L'indice PnP è solo
  **effimero** e usato in memoria per distinguere i device.
  Se un report non dispone di una prova PnP completa, il
  batch **non inventa** il numero di dispositivi distinti.
- Gli archivi OEM in formato **EXE/MSI** non vengono
  eseguiti o aperti come pacchetti driver; usare, quando
  possibile, un ZIP/CAB originale del produttore.

## Test / QA

La suite verifica 12 pacchetti, cap 80, round-robin che non
nasconde NVIDIA anche dopo 80 Intel, confronto dei **veri Device ID
solo in memoria**, due GPU con nome generico uguale, pacchetti
NVIDIA ripetuti con versioni diverse, confronto numerico e
precedenza della compatibilità Windows, conteggi dei dispositivi
distinti e delle associazioni duplicate, parzialità e limiti,
occultamento dei nomi INF/numero ordine e confronto reale di
ZIP sintetici con Hardware ID NVIDIA/Killer/Realtek. CI Windows verifica build desktop, servizio,
Core, test e contratto UI/IPC.

**Da validare sul Recoil 16:** confrontare insieme i ZIP/CAB ufficiali
della GPU NVIDIA, audio NVIDIA, Killer e chipset, poi confrontare
le evidenze con Driver Easy (issue **#91, #136**).
Il numero reale di driver aggiornabili rimane **non verificato**:
qui si recuperano corrispondenze su pacchetti ufficiali
già ottenuti dall'utente, non un feed di nuove release OEM.
