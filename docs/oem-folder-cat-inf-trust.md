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
- Massimo **384 INF / 96 MiB** per cartella, **512 directory**,
  **16 livelli**, **4 MiB per INF**, **32 MiB per CAT**.
  I singoli ZIP OEM mantengono il limite di 96 INF / 24 MiB.
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

## Correzione matching oltre 500 ID INF (#226, v0.0.108)

Gli INF OEM di GPU NVIDIA, Intel/Killer e audio Realtek possono
dichiarare centinaia di modelli: `ReadDeclaredIds` restituisce
**solo i primi 500 ID per l'anteprima**. Fino alla v0.0.107 il piano
di verifica **firme e file driver** usava quella preview come filtro:
l'INF con il modello del PC al 501° ID poteva essere escluso
dall'audit CAT anche se il confronto OEM precedente lo aveva trovato.

`OemFolderCatalogAuditPlan.Create` ora usa la lettura mirata
`InfModelEvidence.ReadMatchingIds(text, dispositivi)`, la stessa
del confronto OEM, che attraversa tutto l'INF limitato e conserva
solo gli Hardware ID/Compatible ID del PC. Soglie:
**2048 dispositivi**, **4096 corrispondenze per INF**, **384 INF**
per cartella, **4 MiB per INF**, **96 MiB cumulativi** e
**12 coppie INF/CAT** massimo per verifica esplicita (dalla #236).

Un file INF con direttive malformed o troppe corrispondenze mirate
è classificato **illeggibile/scartato**: non si verifica alcun CAT
sulla base di evidenza parziale, mentre gli altri INF validi della
cartella continuano a essere ispezionati. `UnreadableInf`
e `IsComplete=false` rendono visibile la copertura parziale.

**Importante:** il matching completo mirato serve soltanto a
decidere quali coppie INF/CAT controllare; non verifica
Windows TargetOSVersion, origine OEM, installabilità, class/extension
DCH, firma CAT o ranking SetupAPI. Un CAT sintetico nei test non è
attendibile e non può diventare un aggiornamento installabile.

## Correzione #236 — verifica CAT/INF allineata a 384 INF (v0.0.113)

In v0.0.112 il confronto di cartelle OEM già estratte era stato esteso
a **384 INF** in quattro lotti, ma il controllo successivo
**Verifica firme e file driver** rifiutava ancora la cartella al
97° INF. Ora il piano di verifica percorre la stessa cartella bounded,
fino a **384 INF / 96 MiB complessivi** (4 MiB per INF),
mantenendo i limiti di **512 directory / 16 livelli** e
bloccando symlink, junction e file CAT collegati.

La verifica dei cataloghi è costosa e continua ad avere un massimo
di **12 coppie INF/CAT** e **8 payload SYS/DLL** per richiesta,
con timeout di 180 s. Se ci sono più di 12 coppie pertinenti,
le prime 12 **non** sono più scelte per ordine di visita:
si preferiscono le coppie che coprono il maggior numero di
**dispositivi PnP distinti non ancora rappresentati**.
Soltanto dopo aver coperto tali dispositivi si assegnano gli slot
rimanenti, in ordine, ad altre coppie. In questo modo una lunga
serie di INF Intel per lo stesso dispositivo non oscura
NVIDIA GPU, Killer Wi-Fi o Realtek audio nel controllo CAT.

La selezione usa soltanto **ordinali temporanei** dell'inventario,
mai ID di istanza o percorsi nel report. I file rimandati sono
conteggiati in `DeferredInf` e non diventano «verificati».
Il riepilogo resta una prova **parziale e read-only**: firma CAT
valida e appartenenza del singolo INF non attestano tutti i
SYS/DLL, la compatibilità OEM, il ranking SetupAPI o la possibilità
di aggiornare. Nessuna nuova installazione, download o
interrogazione di cataloghi NVIDIA/Intel/Realtek viene aggiunta.

Il confronto manuale con i pacchetti reali del Recoil 16
resta necessario per #91 e #136. Le fixture CI non sono
una verifica di firma su CAT autentici.

## Verifica automatica

- [x] Selezione di soli INF che corrispondono all'hardware,
  con CAT esattamente nella stessa directory.
- [x] CAT padre o CAT non dichiarato non diventano firme valide.
- [x] INF corrotto scartato senza nascondere le altre verifiche.
- [x] Budget massimo 12, overflow 384, junction/symlink,
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
