# Confronto CAB OEM senza eseguire installer — fase #122

## Scenario

OmegaDrive v0.0.58 confrontava soltanto gli INF già presenti in un
**ZIP** locale. Molti driver PCSpecialist, NVIDIA, Intel e OEM si
distribuiscono anche in **CAB**. Un CAB contiene un indice di file e
blocchi compressi: l'indice esistente in OmegaDrive non leggeva i
singoli INF.

Questa fase integra l'utilità ufficiale **Windows expand.exe** solo
per leggere gli INF dichiarati in un CAB selezionato dall'utente:

`expand.exe <cab locale> -F:*.inf <cartella temporanea>`

Fonte Microsoft:
https://learn.microsoft.com/it-it/windows-server/administration/windows-commands/expand

Non viene eseguito **nessun** EXE del produttore, setup, MSI, SYS,
script o INF, né viene installato alcun pacchetto. Microsoft
`expand.exe` estrae soltanto INF in una cartella temporanea
appositamente creata, eliminata dopo il confronto.

## Workflow della schermata principale

1. **Analizza il PC** e verifica che l'inventario dei dispositivi
   Windows sia disponibile.
2. Scarica personalmente dal sito ufficiale del produttore uno ZIP
   o CAB destinato alla **configurazione esatta** del computer.
   Su PCSpecialist si apre il portale ufficiale e il numero d'ordine
   viene inserito soltanto **sul loro sito**.
3. Seleziona **Confronta ZIP / CAB** dalla schermata risultati.
   L'app richiede conferma prima di leggere il file.
4. Con ZIP mantiene il percorso di v0.0.58, in memoria. Con CAB:
   - riconosce solo l'estensione .cab e valida prima tutto l'indice
     mediante `OemCabIndex.Inspect`;
   - rifiuta CAB multipart, nomi con traversal, percorsi ambigui,
     troppi file INF e dimensioni non consentite;
   - avvia **solo** `%SystemRoot%\System32\expand.exe`
     con argomenti non interpretati da una shell, un pattern
     fisso `-F:*.inf` e timeout di due minuti;
   - verifica i file estratti: solo .inf, nomi corrispondenti
     a quelli dichiarati nel CAB, niente symlink/reparse point,
     dimensioni identiche a quelle indicate nell'indice;
   - inserisce i soli INF validati in uno ZIP temporaneo **in
     memoria** e riutilizza `LocalOemDriverComparison`.
5. OmegaDrive mostra una riga per dispositivo: Hardware ID
   esatto o Compatible ID, versione locale/INF, architettura,
   build Windows minima e un eventuale numero versione superiore.
6. Questi risultati rimangono nella sezione
   **Confronto pacchetto OEM locale — non installabile**,
   e non entrano in Windows Update né in **Aggiorna tutto**.

## Limiti / minacce

- Massimo CAB 1 GiB, 2.048 file indicizzati, 96 INF,
  1 MiB/INF e 24 MiB complessivi non compressi.
- Timeout `expand.exe` 120 secondi, tentativo di terminazione
  del processo se cancellato, directory temporanea nuova e
  pulizia nel blocco `finally`.
- I dati rimangono sul computer; non si raccoglie numero ordine,
  seriale né identificativi PnP nel report aggregato.
- **EXE e MSI sono rifiutati**. I formati autoestraenti
  non vengono avviati o analizzati fingendo che siano CAB.
- Versione numericamente maggiore e presenza di file CAT
  **non dimostrano** compatibilità SetupAPI, ranking, firma
  valida, provenienza, dipendenze o sicurezza. Non vengono
  estratti file .cat/.sys/.dll né eseguite installazioni.
- CAB con struttura insolita, non supportata o con INF
  non leggibili deve generare **errore di verifica**, non
  «nessun driver da aggiornare».
- Gli errori possono verificarsi anche quando alcuni file
  temporanei restano bloccati da antivirus/indicizzazione.
  Non verranno eseguiti.

## Test Windows

Oltre ai test della funzione ZIP esistente, la nuova suite
`LocalOemCabComparisonTests` crea un vero CAB con lo
strumento Windows **makecab.exe**, quindi chiama il nuovo
comparatore che utilizza Windows **expand.exe**:

- [x] INF NVIDIA RTX 4060 Laptop con Hardware ID esatto,
  architettura/build coerenti e DriverVer maggiore.
- [x] CAB con INF non pertinente al dispositivo: nessun match.
- [x] CAB che non dichiara INF: report informativo non aggiornabile.
- [x] EXE/MSI con estensione non ammessa: nessun tentativo di esecuzione.
- [x] CAB con header errato: blocco prima del processo Windows.
- [x] Cancellazione prima dell'analisi: nessuna estrazione.
- [x] Validazione statica struttura WPF e separazione dalle
  azioni rapide Windows Update.

La CI può validare un CAB sintetico reale, **non** la
compatibilità di CAB OEM specifici della GPU/portatile.
Collaudo su PCSpecialist Recoil 16 ancora richiesto con
CAB o ZIP scaricati dal proprietario, senza condividere
numero d'ordine o seriali.

## Successivi interventi

- Supporto a pacchetti EXE **soltanto** se esiste un formato
  embedded riconosciuto da un parser sicuro e senza avviare
  alcun installer. Diversamente restano non analizzabili.
- Firma CAT/SYS/INF e ranking Windows con strumenti ufficiali
  in una fase separata, prima di qualsiasi installazione OEM.
- Confronto di driver concreti NVIDIA, Intel/Killer, audio,
  chipset e periferiche realmente pubblicati per il Recoil,
  con baseline #91 per misurare la copertura in sicurezza.
