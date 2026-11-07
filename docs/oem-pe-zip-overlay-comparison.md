# OmegaDrive — pacchetti EXE OEM con ZIP incorporato (issue #216)

## Cosa cambia

In precedenza OmegaDrive confrontava con i dispositivi Windows
soltanto i pacchetti locali **ZIP/CAB** e le cartelle driver già
estratte. Alcuni produttori distribuiscono driver tramite eseguibili
autoestraenti. Per una sottoclasse verificabile, il file EXE contiene
un vero archivio **ZIP standard concatenato dopo le sezioni PE**.

Dalla nuova versione, dopo **Analizza il PC** selezionare
**Dettagli avanzati → Confronta ZIP / CAB / EXE multipli**,
scegliere da 1 a 12 file scaricati dal produttore e confermare
la lettura in sola lettura. **OmegaDrive non esegue mai l'EXE.**

Il supporto EXE non è generico. In particolare i formati
**7z/7-Zip SFX, NSIS, Inno Setup, archivi CAB annidati e
installer proprietari** non vengono interpretati né lanciati.
Se non è presente un vero ZIP standard verificabile,
la richiesta fallisce in modo esplicito, non produce il
messaggio fuorviante «zero driver» e non tenta fallback
pericolosi.

## Pipeline tecnica e protezioni

1. Verifica formato **PE Windows** (MZ, firma PE, tabella
   sezioni COFF, x86/x64/ARM64), offset dell'header massimo
   **1 MiB** e al massimo **96 sezioni**. Controllo delle
   dimensioni dichiarate delle sezioni e della loro
   posizione nel file.
2. Individua l'area **dopo la fine di tutte le sezioni PE**.
   Non confonde firme ZIP presenti in `.text` o `.rsrc`
   con un payload driver. Esamina fino a **16 MiB** di
   preambolo dell'overlay: se non trova uno ZIP standard,
   dichiara il formato non supportato. EXE intero massimo
   **1 GiB**, come per gli altri archivi.
3. Apre una finestra **seekable e read-only** sullo ZIP
   aggiunto, senza copiare in RAM o estrarre l'EXE completo.
   La finestra non consente alcuna scrittura e non trasferisce
   il controllo al programma contenuto nel PE.
4. Impone l'indice ZIP esistente
   `OemArchiveInspector`: massimo **2048 entry**, path
   traversal/symlink/duplicati bloccati, limiti di dimensione
   e rapporto compressione. Errore ZIP → niente corrispondenze
   pubblicate come valide.
5. Riusa `OemZipInfEvidence` e
   `LocalOemDriverComparison`: massimo **96 INF**,
   **4 MiB per INF**, **24 MiB** di INF decompressione,
   massimo 80 righe con priorità e fairness per vendor,
   matching hardware e preflight Windows già esistenti.
   Nessun SYS/DLL/firmware eseguito o installato.
6. Sostituisce i nomi originali degli INF presenti nell'EXE
   con etichette anonime `EXE ZIP · INF 001`. I nomi
   dei file possono includere numeri ordine, account o
   percorsi locali. La UI non invia nulla ai produttori.
7. Nel batch fino a **12** pacchetti ZIP/CAB/EXE con cap
   cumulativo **4 GiB** e timeout globale **5 minuti**,
   un EXE malformato fa fallire l'intero batch senza
   pubblicare risultati parziali.

## Cosa NON significa

Un INF che dichiara lo stesso Hardware ID e una versione
numericamente superiore rappresenta **solo un indizio**.
Non prova provenienza del pacchetto, firma CAT, integrità
SYS/DLL, ranking SetupAPI o applicabilità definitiva
di una personalizzazione OEM notebook.

Tutti i report rimangono
`IsInstallable = false` /
`IsVerifiedCompatible = false`; nessun nuovo GUID WUA,
nessuna modifica a `Update`/`Aggiorna tutto`, nessun
download, esecuzione installer o upload.

Questa feature può riconoscere driver NVIDIA, Killer,
Realtek o altri soltanto se già contenuti in un
**vero EXE/ZIP** ottenuto da una fonte ufficiale.
Non promette compatibilità con i normali installer
NVIDIA distribuiti in altri formati.

## Prove e QA

I test automatici usano un **PE sintetico** con firma
MZ/PE, sezione binaria e ZIP standard appeso con
INF NVIDIA e Killer. Verificano che la falsa firma ZIP
in `.rsrc` sia ignorata, che pacchetti e percorsi
corrotti, PE invalidi, ZIP traversal, bomb di
compressione e altri formati falliscano chiusi.
Il parser non avvia eseguibili in nessun ramo.

**QA reale ancora necessaria:** scegliere un EXE OEM
ZIP autentico dal portale PCSpecialist e verificare
gli INF trovati sul Recoil 16. Il confronto 2 contro
8 candidati (#91, #136) resta aperto.
