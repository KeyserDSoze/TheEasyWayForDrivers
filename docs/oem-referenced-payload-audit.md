# Audit dei file SYS/DLL referenziati dagli INF OEM — issue #130

## Motivazione

Nel ramo v0.0.62 OmegaDrive può identificare INF appartenenti a
un CAT firmato mediante Windows/SignTool del Windows SDK già presente.
L'INF, tuttavia, può richiamare altri file tramite CopyFiles,
SourceDisksFiles, Include, Needs o direttive servizi. Un INF con
firma corretta e numero versione superiore **non dimostra** che
Windows possa o debba installare il pacchetto.

Fonti Microsoft:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/driver-store
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-windows-ranks-driver-packages
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/overview-of-the-driver-selection-process
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/verifying-the-release-signature

## Modifica della funzione guidata

Dopo **Analizza il PC** → **Confronta cartella driver**, se ci
sono corrispondenze PnP compare **Verifica firme e file driver**
nella sezione OEM non installabile. L'utente conferma
esplicitamente questa seconda operazione.

La procedura:
1. individua al massimo 12 coppie INF/CAT correlate al PC,
   con i limiti allineati a quelli del confronto cartelle
   (384 INF, 512 cartelle, 16 livelli,
   4 MiB/INF, 96 MiB di dati INF);
2. verifica firma CAT con Windows PowerShell e membership
   di ogni INF mediante SignTool del Windows SDK **solo se
   già installato** (non lo scarica);
3. per ogni INF, usa la **preview conservativa** dei riferimenti
   CopyFiles/SourceDisksFiles/Include/Needs già implementata;
4. legge solo i **nomi e le dimensioni** dei file già presenti,
   senza eseguire SYS, DLL, EXE, MSI, script o installer;
5. controlla al massimo **1024 voci locali** per directory-pacchetto,
   **8 livelli** sotto la cartella CAT, e seleziona fino a **8**
   file potenzialmente richiesti per INF;
6. soltanto se **sia CAT sia INF** hanno superato il
   controllo, usa SignTool \`verify /kp /c CAT FILE\` anche
   per i file selezionati, per un **massimo globale di 8
   payload verificati** per richiesta;
7. mostra i conteggi **senza percorsi**: file potenziali
   presenti/mancanti/non risolti, controlli eseguiti,
   respinti, non verificati, rinviati, INF non analizzabili
   e INF con avvertenze.

Un controllo non risolvibile, mancanza di Windows SDK,
file troppo grande, archivio insolito o limite raggiunto
**non** diventa una verifica positiva. Se un file manca,
questo è un potenziale file mancante secondo la preview
dei possibili modelli/install sections, **non** una prova
che Windows lo avrebbe richiesto per questo dispositivo.
Se un file è presente, non è per questo automaticamente
autentico o installabile.

## Correzione #238 — INF grandi e cartelle con molti file (v0.0.114)

La preview dei SYS/DLL e la verifica individuale di appartenenza
INF al CAT conservavano due vecchi limiti della prima integrazione:
**1 MiB per INF** e **256 voci directory**. Dopo la #234/#236
potevano quindi rifiutare il controllo su pacchetti già legittimamente
riconosciuti dal comparatore (**4 MiB per INF, 384 INF per cartella**).

Adesso `InfDependencyPreview.MaxInfCharacters` e
`WindowsInfCatalogMembershipReader.MaxInfBytes` derivano dal limite
condiviso `OemZipInfEvidence.MaxInfBytes = 4 MiB`. La preview
mantiene comunque le sue protezioni indipendenti: massimo
**12.000 righe**, **160 sezioni**, **80 riferimenti** e
limiti sulle direttive. Se queste soglie sono superate,
il risultato è non analizzato, mai una verifica positiva.
La lettura del CAT resta massimo **32 MiB**.

Solo la scansione read-only degli elementi sotto la cartella
CAT passa da **256 a 1024 nomi filesystem**, così 384 INF
e gli eventuali SYS/DLL possono coesistere. Non si modifica
la whitelist delle estensioni né il budget globale di **8
verifiche payload**; mancanza/ambiguità o limiti superati
restano casi non verificabili. Anche un file individualmente
membro del CAT non valida tutta la catena del pacchetto.

I test includono un INF reale sintetico >1 MiB con
CopyFiles, una cartella con 384 INF e payload referenziati,
overflow oltre 1024, privacy nei report, e le precedenti
prove negative su symlink e cancellazione.

## Il ranking di Windows: cosa non si deve inventare

Microsoft documenta che il valore di ranking dipende da
firma, features e match dell'ID con il dispositivo. Windows
sceglie il **rank numericamente più basso**; a parità di
rank usa data e poi versione.

I dettagli di rank sono osservabili, per esempio, nel
log SetupAPI **di selezioni eseguite realmente da Windows**.
Il nuovo audit di una cartella OEM non ancora staged non
possiede questo ranking: OmegaDrive lo segnala
**non calcolato** e non confronta versioni per decidere
un'installazione.

**Questa fase NON invoca SetupAPI per installare, non
copia nulla nel Driver Store e non crea driver node
staged.** Per classificare realmente pacchetti esterni
servirà un percorso distinto con autorizzazioni,
risoluzione INF e API di sistema documentate.

## Protezioni e limiti

- Read-only su tutto il filesystem e nessun invio rete.
- I riferimenti sono limitati alle cartelle già selezionate;
  symlink, junction/reparse point e percorsi con \`..\`
  vengono respinti.
- Una cartella OEM con più di **1024 voci** non viene auditata
  da questo passaggio: il risultato è **non analizzato**,
  non «nessun file mancante». Il limite è specifico della preview
  dei file referenziati nella cartella: il vecchio limite 256
  della verifica indipendente dei payload non è modificato.
- File payload max 64 MiB per elemento per la verifica
  (soglia \`DriverPayloadAuditPolicy\`).
- Timeout complessivo dell'audit guidato 180 secondi,
  tool Windows SDK con timeout per verifica.
- I percorsi scelti, Device Instance ID, Hardware ID e
  numero d'ordine PCSpecialist non vengono inseriti nel
  report aggregato.
- La verifica SignTool positiva di 1 file dice soltanto
  che quel file è membro del CAT secondo la policy
  applicata **in quel momento**. Non certifica tutti i
  SYS/DLL, le dipendenze esterne, il package intero,
  la sua provenienza, il ranking SetupAPI, né compatibilità
  o necessità dell'aggiornamento.
- Nessun file OEM viene inserito in **Aggiorna tutto**.

## Test e QA

Test automatici:
- [x] Due payload presenti e uno potenzialmente mancante,
  verifica nomi sorgenti invece di destinazioni rinominate.
- [x] File non referenziati non verificati accidentalmente.
- [x] Riferimenti non risolti distinti dai file mancanti.
- [x] Riepiloghi distinti per file membro del CAT,
  respinto/non verificato, verifiche rinviate.
- [x] Numero incongruo di esiti respinto.
- [x] Inventario superiore a 1024 voci **non analizzato**
  senza affermazioni di completezza.
- [x] Cartella di **384 INF** con due payload SYS/DLL correlati:
  il riepilogo li riconosce senza classificarli installabili.
- [x] INF da **oltre 1 MiB e meno di 4 MiB** con riferimenti
  CopyFiles riconosciuti; stesso budget dei lettori OEM e del CAT.
- [x] Directory symbolic link e CAT di altra cartella
  respinti.
- [x] Verifiche annullabili.
- [x] UI: pulsante opt-in nella sezione OEM non installabile.
- [ ] CI Windows build/test del PR.
- [ ] Prove con CAT realmente firmati OEM sul
  PCSpecialist Recoil 16 e Windows SDK installato.

Resta aperto il confronto #91 tra OmegaDrive e Driver Easy
per NVIDIA RTX 4060 Laptop, NVIDIA HD Audio,
Platform Controllers, Intel/Killer/Wi-Fi/Bluetooth.
Il presente lavoro migliora le **prove di fiducia**, non
il numero di candidati automaticamente scoperti da WUA.

Rif #130 #128 #122 #91.
