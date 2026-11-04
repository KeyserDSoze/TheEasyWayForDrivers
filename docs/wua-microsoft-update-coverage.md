# Copertura Windows Update + Microsoft Update: controlli e test (#110 #111)

## Problema e ipotesi da verificare

L'inventario Windows PnP può contenere dispositivi con driver non
aggiornati anche quando Windows Update presenta soltanto 1–2 aggiornamenti.
La situazione descritta nell'issue #91 (circa 8 candidati in Driver Easy
contro circa 2 in OmegaDrive) **non è una prova** che siano tutti update
installabili o preferibili. Dobbiamo distinguere:
- **dispositivo presente** in PnP con versione installata;
- **candidato WUA realmente offerto** per il computer;
- **versione OEM pubblicata** (non necessariamente applicabile);
- **driver OEM necessario** secondo uno strumento ufficiale del produttore;
- **fonte non interrogata** o fallita.

Questa fase amplia la ricerca WUA senza inventare candidati.

## Microsoft Update come ulteriore sorgente WUA

Microsoft distingue Windows Update e Microsoft Update con due ServiceID
documentati:
- Windows Update: `9482f4b4-e343-43b6-b170-9a65bc822c77`
- Microsoft Update: `7971f918-a847-4430-9279-4a52d1efe18d`

La modalità **Comprehensive** interroga come in precedenza il servizio
configurato e Windows Update diretto, ciascuno in modalità ordinaria e
avanzata. Legge poi (senza modificare nulla) le registrazioni attuali
via `Microsoft.Update.ServiceManager.Services`.

Se Microsoft Update è già registrato, aggiunge:
1. `ServerSelection=3` (`ssOthers`), `ServiceID=MicrosoftUpdate`,
   `IsInstalled=0 and IsHidden=0 and Type='Driver'`.
2. Stessa fonte con `IsInstalled=0 and Type='Driver'` e
   `IncludePotentiallySupersededUpdates=true`, classificando i
   risultati come **avanzati**. Non vengono preselezionati da
   Aggiorna tutto.

Se Microsoft Update **non è registrato**, il report indica
`NotRegistered` e non esegue la ricerca. Se l'elenco dei servizi
WUA non è accessibile, il report indica `Failed`, senza riportare
falsi zero. OmegaDrive **non** chiama `AddService`,
`RegisterServiceWithAU`, `SetOption`, né cambia policy WSUS.

È possibile che Microsoft Update offra *zero nuovi candidati* rispetto
alla sorgente configurata: non va presentato come nuova disponibilità.
Inoltre la gamma di driver NVIDIA/OEM può differire dai cataloghi
commerciali: MU non garantisce la parità con Driver Easy.

### Dedup e installazione

Ogni pass WUA restituisce un numero lordo di candidati; il report
espone anche **NewUniqueCandidates**, la quantità di nuovi
`UpdateID` rispetto a tutti i pass WUA già completati.
Lo stesso update visto da due fonti compare una sola volta. Un
risultato ordinario prevale su uno avanzato dello stesso ID.

Prima di qualunque installazione l'aggiornamento selezionato viene
**ritrovato per UpdateID** tramite Windows Update Agent. Si prova
il servizio configurato, poi Windows Update diretto, quindi (solo se
registrato) Microsoft Update. Nessun download da URL OEM,
nessun bypass firma, e i risultati OEM restano separati.

### Test automatici

- [x] Solo Recommended -> un pass configurato.
- [x] Comprehensive -> quattro pass originari se MU assente,
  sei pass se già registrato.
- [x] `ssOthers` e GUID esatto; niente software o registrazione
  silenziosa di servizi.
- [x] Dedup tra fonti (case-insensitive) e precedenza delle
  raccomandazioni ordinarie.
- [x] `NotRegistered` distinto da `Failed`; non gonfia conteggi
  né fa sembrare parziale la scansione degli altri servizi.
- [x] Scenario sintetico #91: 8 dispositivi PnP principali
  (NVIDIA GPU/audio/platform, Intel grafica/Killer/Bluetooth,
  Realtek e MediaTek) con 2 WUA candidati: visualizzati come
  *8 dispositivi riconosciuti / 2 correlazioni WUA*, **non**
  *8 aggiornamenti*. Un WUA assente non vale «driver aggiornato».

## Protocollo QA reale per valutare più driver

Su un Windows 11 reale con NVIDIA RTX 4060 Laptop GPU e Killer:
1. Annotare build Windows, produttore/modello, driver GPU,
   Bluetooth, Killer e audio da Gestione dispositivi. Non
   pubblicare seriali o ID PnP di istanza univoci.
2. Avviare la ricerca OmegaDrive **Comprehensive** e salvare
   soltanto conteggi di servizio e versioni (no seriali).
3. Leggere in `Fonti controllate...` lo stato di ciascuna fonte
   (`interrogata` / `non registrata` / `fallita`) e i
   nuovi `UpdateID` per servizio. Valutare in particolare il
   contributo Microsoft Update, che può essere 0.
4. Confrontare i singoli *driver candidati* di Driver Easy con
   la versione già installata, il device Hardware ID del
   modello, l'OS, la fonte ufficiale e il pacchetto OEM.
   Non considerare il solo numero di risultati come qualità.
5. Su PC Dell/Lenovo/HP (strumento ufficiale già installato)
   eseguire le scansioni OEM dedicate per validare le differenze:
   i risultati OEM non entrano automaticamente in Aggiorna tutto.
6. Verificare i casi limite: Windows senza MU registrato,
   WSUS/proxy offline, Microsoft Update che risponde con
   risultati duplicati, una fonte che fallisce e altre che
   riescono, e driver NVIDIA generici `PCI\\VEN_10DE`,
   `HDAUDIO\\...VEN_10DE`, `ACPI\\NVDA...`.

Riferimenti ufficiali:
- https://learn.microsoft.com/en-us/windows/deployment/update/how-windows-update-works
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-get_serviceid
- https://learn.microsoft.com/en-us/previous-versions/windows/desktop/aa387280(v=vs.85)
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-iupdateservicemanager

**Limite:** la CI unit-test non simula la pubblicazione degli
aggiornamenti su server Microsoft; la QA hardware reale resta
aperta. Nessuna affermazione «trova X driver aggiuntivi» è
giustificata senza i risultati del computer.
