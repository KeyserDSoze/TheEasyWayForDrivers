# Confronto cartelle driver OEM: INF locali senza eseguire EXE (#124)

## Problema

Molti driver del **PCSpecialist Recoil 16**, NVIDIA, Intel/Killer e
altri produttori non sono distribuiti come ZIP o CAB semplice,
ma tramite installatori EXE, archivi estratti dal sito del produttore,
oppure cartelle con file INF/CAT/SYS.

OmegaDrive non esegue EXE per "cercare" driver. Dalla v0.0.60
introduciamo invece un terzo percorso esplicito:
**Confronta cartella driver**, che usa soltanto gli INF **già presenti**
su disco. Non installa né esegue nessun file del produttore.

## Flusso

1. Avvia **Analizza il PC** per ottenere il vero inventario PnP.
2. Procurati dal sito ufficiale un pacchetto relativo al modello esatto.
   Se PCSpecialist richiede un numero d'ordine, inseriscilo esclusivamente
   sul suo sito; non trasmetterlo a OmegaDrive o nella issue GitHub.
3. Se il produttore già distribuisce file INF in cartelle, oppure hai
   **già estratto autonomamente** il pacchetto con un metodo documentato,
   premi **Confronta cartella driver**. Non eseguire un installer
   sconosciuto soltanto per ottenere i file.
4. Seleziona una directory e conferma la lettura read-only.
5. OmegaDrive esamina fino a **384 INF nei sottopercorsi**, divisi in **massimo quattro lotti da 96 INF/24 MiB** ciascuno, e
   riutilizza il confronto già collaudato per ZIP/CAB:
   Hardware ID esatto, Compatible ID, SUBSYS, TargetOSVersion,
   architettura Windows, DriverVer e versione installata.
6. Mostra una riga **per dispositivo**, non un aggiornamento per INF.
   Versioni numericamente superiori sono soltanto suggerimenti:
   **CAT/firmatario, package source, SetupAPI ranking e dipendenze
   non sono ancora attestati**.
7. La sezione resta **Confronto pacchetto OEM locale — non installabile**.
   Il pulsante **Aggiorna tutto** non comprende mai i file di cartella.

## Protezioni

- Il processo non esegue né carica EXE, MSI, BAT, CMD, DLL, SYS,
  CAT o altri installer; non estrae archivi, non scrive nella cartella.
- Solo file `.inf` vengono letti. Massimo **384 INF** nella cartella,
  suddivisi in un massimo di **4 lotti** da **96 INF/24 MiB**
  ciascuno, con **4 MiB per INF** e **96 MiB globali**.
  Restano invariati **512 directory** e **16 livelli** di profondità.
  I singoli ZIP/CAB analizzati separatamente mantengono i propri limiti.
- I file INF sono ricopiati in **ZIP temporanei in memoria, uno per lotto**, con
  nomi generati e limitati, poi passati allo stesso validatore PnP
  e INF di ZIP/CAB. Nessun file viene inviato online.
- Junction e symbolic link sono rifiutati prima di entrare in una
  directory o leggere un INF, per non seguire percorsi esterni.
- Le dimensioni sono ricontrollate mentre gli INF vengono letti,
  per impedire che un file modificato durante l'analisi aggiri
  i limiti. Timeout/annullamento della scansione UI già supportati
  dal flusso `RunBusyAsync`.
- **Zero INF** non significa "tutti i driver aggiornati": significa
  che nella directory selezionata non c'erano INF leggibili.
- Se la cartella è il risultato di un EXE autoestraente, OmegaDrive
  analizza solo i file **già estratti**: non avvia l'EXE per ottenerli.
- Nessun seriale, numero ordine o percorso del profilo Windows
  viene inserito nel report diagnostico copiabile.

## Test

- [x] Fixture multi-directory con NVIDIA RTX 4060 Laptop GPU,
  NVIDIA HD Audio e Killer Wi-Fi: tre corrispondenze indipendenti,
  nessun EXE eseguito.
- [x] Due INF per un device: un dispositivo, non due aggiornamenti.
- [x] INF non pertinente: nessuna correlazione fittizia.
- [x] Solo setup.exe: zero INF ma situazione OEM **non verificata**.
- [x] Da 97 fino a 384 INF: quattro lotti al massimo, tutti confrontati
  senza ampliare i limiti del lettore di singolo archivio.
- [x] Uno stesso dispositivo NVIDIA in più lotti viene contato una sola
  volta tramite ordinale PnP effimero; vince la migliore evidenza disponibile.
- [x] Killer/Realtek dopo i primi 96, INF decodificabile e INF scartato,
  oltre 80 dispositivi (conteggio distinto corretto, 80 righe visibili).
- [x] Oltre 384 INF, oltre 4 MiB per INF e oltre budget: errore
  esplicito, **nessun report parziale** spacciato per esaustivo.
- [x] Directory annidate troppo profonde: rifiuto.
- [x] Cancellazione richiesta e directory inesistente: rifiuto.
- [x] Symbolic directory: rifiuto dove il filesystem lo supporta.
- [x] Contratto statico XAML: pulsante visibile solo dopo
  inventario e separato dalle azioni installabili.

## Incremento #234 — cartelle OEM grandi in lotti

Un bundle già estratto con 97 INF causava un errore completo anche
quando i 96 file precedenti erano legittimi. Da questa versione il
confronto rimane **in sola lettura** e rielabora gli stessi INF in
4 batch bounded, mai più di 96 INF/24 MiB per lotto. Dopo ogni lotto
il ZIP in memoria viene eliminato. La deduplicazione inter-lotto
usa **solo ordini numerici temporanei dei dispositivi PnP**: non
somma gli stessi dispositivi presenti in più cartelle e non divulga
Device Instance ID/seriali. I risultati di lotti multipli mostrano
riferimenti numerici generici ai lotti invece dei nomi delle cartelle
originali.

Nel riepilogo viene dichiarato il numero totale di INF esaminati,
quelli scartati, i dispositivi distinti e quelli oltre il limite
di 80 righe. Se un INF non è decodificabile o il report è limitato,
la copertura rimane esplicitamente **parziale**. Se viene superato
il limite globale (384 INF, 96 MiB o quattro lotti), la scansione
fallisce senza consegnare un falso risultato parziale/positivo.

Questa funzionalità non interroga servizi remoti, non esegue setup
OEM né verifica firme, ranking o aggiornamenti realmente disponibili.
Il confronto su hardware Recoil 16 richiede ancora prove (#91, #136).

## QA ancora aperta

- [ ] Test sul PCSpecialist Recoil 16 reale con cartelle driver
  della configurazione acquistata.
- [ ] Valutare driver NVIDIA Platform Controllers, HD Audio,
  Killer/Intel e Bluetooth rispetto alle versioni installate.
- [ ] QA visiva DPI 100/125/150% e computer con migliaia di
  directory OEM.
- [ ] Correlare i pacchetti al ranking effettivo SetupAPI e
  verificare il CAT firmato in una fase **separata** prima
  di autorizzare un eventuale installatore.

Questo incrementa la **copertura delle fonti locali**,
non garantisce un aumento di candidati Windows Update e
non risolve da solo il confronto con Driver Easy #91.

Riferimenti: #124 #122 #120 #91 #100.


## Privacy dei nomi INF originali (#419)

Un file OEM locale può chiamarsi anche `PRIVATE_ORDER_98234_GPU.inf` o contenere il nome utente: sostituire caratteri speciali con `_` **non** anonimizza i dati alfanumerici. Il confronto cartelle inserisce gli INF nei ZIP temporanei in memoria usando soltanto nomi **generati** del tipo `d001/inf0001.inf`, con un ordinale globale fino a 384 INF. Il basename e il percorso originali restano usati esclusivamente per aprire il file locale in sola lettura e **non** vengono trasferiti alla voce ZIP, al parser o alla proprietà `Finding.InfName` visualizzata nella UI.

Il progressivo globale evita collisioni anche tra cartelle con INF omonimi e tra lotti da 96 elementi. La modifica non altera conteggi, corrispondenze PnP/INF, versioni, limiti di lettura né la policy non installabile. I test verificano due file omonimi in directory con numeri d'ordine e nomi privati, e un INF mirato oltre il primo lotto; nessun originale compare nel report serializzato. La QA Windows hardware (#136) e la firma Authenticode (#283) rimangono interventi separati.

## Perimetro locale dell'intero percorso OEM (#422)

Il confronto read-only di cartelle e l'audit firme INF/CAT devono esaminare **lo stesso perimetro locale**. Prima dell'analisi, `LocalOemFolderComparison.Compare` ora rifiuta radici UNC (incluso il namespace esteso `\\\\?\\UNC\\...`) **prima di verificare l'esistenza del percorso**: non tenta più l'accesso a condivisioni remote con questo ingresso. Dopo `Path.GetFullPath`, verifica inoltre `ReparsePoint` non soltanto sulla cartella scelta, ma su **ogni suo antenato fino alla radice**. Questo impedisce che una sottocartella apparentemente ordinaria venga analizzata passando attraverso una junction/symlink superiore. Le verifiche sulle sottocartelle e sui singoli file INF rimangono attive e invariate.

Test Windows: cartella locale con match NVIDIA invariato, UNC senza necessità di rete e sottodirectory sotto un antenato simbolico (se la creazione di link è permessa al runner); controllo del comportamento parallelo di `OemFolderCatalogAuditPlan`. Il confronto resta solo informativo, non installabile: non valida cataloghi CAT, firme, payload SYS/DLL o ranking SetupAPI. QA Recoil 16 e sorgenti OEM reali (#124/#136), e firma Authenticode (#283), rimangono aperte.
