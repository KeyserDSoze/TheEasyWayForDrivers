# Controllo CAT/INF dei pacchetti OEM locali — issue #128

## Problema

In v0.0.61 OmegaDrive può confrontare ZIP, CAB e cartelle INF
con il PCSpecialist Recoil 16 (RTX 4060 Laptop, NVIDIA HDAUDIO,
Intel/Killer), ma la presenza di un file \`.cat\` in una cartella
non dimostra che il singolo INF faccia parte del catalogo, né
che il catalogo abbia una firma valida.

Microsoft documenta che un driver package deve avere un
catalogo **firmato** che contenga gli hash dell'INF e dei file
installati. Il controllo dell'intero pacchetto e la validazione
SetupAPI durante lo staging sono distinti.

- https://learn.microsoft.com/windows-hardware/drivers/install/driver-store
- https://learn.microsoft.com/windows-hardware/drivers/install/catalog-files
- https://learn.microsoft.com/windows-hardware/drivers/install/inf-version-section
- https://learn.microsoft.com/en-us/windows/desktop/seccrypto/signtool

## Funzione nuova

Dopo aver usato **Analizza il PC** e **Confronta cartella driver**,
se il confronto ha trovato dispositivi corrispondenti, compare
nella sezione locale **non installabile** il pulsante
**Verifica firme INF/CAT**.

Il pulsante richiede un **secondo consenso esplicito**:

1. Legge ancora in sola lettura gli INF locali della cartella
   selezionata; non memorizza il percorso tra sessioni e non
   lo mette in report/diagnostica.
2. Si limita agli INF con un Hardware ID o Compatible ID
   presente tra i dispositivi rilevati. Usa la stessa
   \`OemInfTextDecoder\` di v0.0.61.
3. Recupera \`CatalogFile[.NT...]\` dalla sezione \`[Version]\`.
   La coppia INF/CAT viene accettata per l'ispezione solo
   se il CAT è **nella stessa directory** dell'INF,
   con estensione controllata e dimensione massima 32 MiB.
4. Usa il già presente comando built-in Windows PowerShell
   \`Get-AuthenticodeSignature\` per leggere lo stato della
   firma del catalogo.
5. Se Windows riporta CAT con firma valida, usa il già
   presente SignTool del Windows SDK (se installato):
   \`signtool verify /kp /c <catalogo> <INF>\`.
   **Non** installa automaticamente il Windows SDK e
   non usa tool da PATH/working directory non attendibili.
6. Mostra un report **aggregato senza percorsi** con numero
   INF esaminati, INF che corrispondono al dispositivo,
   CAT assenti, INF illeggibili, verifiche sospese per
   limite, cataloghi con firma Windows valida,
   appartenenze INF/CAT validate, verifiche respinte
   e non verificate.

## Sicurezza e limiti

- Nessun \`setup.exe\`, MSI, SYS, DLL o installatore OEM
  viene eseguito. Si avviano solo tool Windows/SDK
  nell'installazione di sistema, in modalità di verifica.
- Non viene chiesto né trasmesso il numero d'ordine
  PCSpecialist, il seriale, l'Hardware ID o la directory
  privata dell'utente.
- Rifiuto di cartelle link/junction e CAT/INF reparse.
- Massimo 96 INF, 512 directory, 16 livelli,
  1 MiB per INF, 24 MiB INF totali, 32 MiB per CAT.
- Per contenere tempo e processi al massimo **12 coppie**
  INF/CAT per esecuzione; eventuali altre coppie sono
  dichiarate **rinviate**, non verificate.
- Timeout complessivo **180 secondi** e cancellazione.
  Windows PowerShell/SignTool ricevono gli argomenti
  separati, non passano attraverso shell interpretative,
  e il loro output grezzo non viene mostrato all'utente.
- Se SignTool non è installato, CAT non valido, CAT
  assente o controllo interrotto, l'INF rimane
  **non verificato / respinto**, mai verificato per
  semplice presenza del file.
- Anche quando sia la firma CAT sia l'appartenenza di
  **uno specifico INF** sono validate, il pacchetto
  rimane **NON installabile** dal flusso OEM.
  Mancano ancora attestazioni di TUTTI i file SYS/DLL,
  di dipendenze, policy/revoca kernel, compatibilità OEM
  e ranking SetupAPI. La fonte del pacchetto non viene
  certificata dal confronto Hardware ID.

## Verifica automatica

- [x] Selezione di soli INF che corrispondono all'hardware,
  con CAT esattamente nella stessa directory.
- [x] CAT padre o CAT non dichiarato non diventano firme valide.
- [x] INF corrotto scartato senza nascondere le altre verifiche.
- [x] Budget massimo 12, overflow 96, junction/symlink,
  cancellation e size limits.
- [x] Report sintetico di firma invalida, SignTool mancante,
  membership non verificata e verifica individuale positiva,
  che **non** prova il pacchetto.
- [x] UI static contract: pulsante nascosto all'avvio,
  opt-in nella sezione OEM non installabile.
- [ ] Validazione CI Windows (compilazione/test).
- [ ] Test su pacchetto reale del Recoil 16 con CAT
  realmente firmato e SignTool Windows SDK installato.
  Le fixture di test usano CAT sintetici **non firmati**
  e non possono dimostrare alcuna firma WHQL.

## Lavori successivi

La fase successiva può valutare, con un'apposita autorizzazione
e controlli Windows, i file SYS/DLL richiesti dal singolo INF,
le dipendenze \`Include/Needs/CopyFiles\` e il ranking SetupAPI
reale **senza installare** o fare staging. Fino ad allora
nessun pacchetto OEM viene inserito in **Aggiorna tutto**.

Riferimenti #128, #122, #91, #120.
