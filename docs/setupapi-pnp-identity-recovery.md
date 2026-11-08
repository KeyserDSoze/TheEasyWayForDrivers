# OmegaDrive — recupero degli ID PnP tramite SetupAPI Windows (#212)

## Quale limite corregge

Nella v0.0.101 il servizio inventaria i dispositivi con
`Win32_PnPEntity`. A volte `HardwareID` e/o `CompatibleID`
sono assenti nella risposta WMI, mentre Windows è ancora in grado
di leggere tali proprietà tramite API native Plug and Play.

Su un notebook Recoil 16, senza identificativi del dispositivo,
OmegaDrive può mostrare solo un generico «Microsoft Basic Display
Adapter» e non correlare un eventuale driver NVIDIA effettivamente
restituito da Windows Update. Lo stesso problema può riguardare
Killer Wi-Fi, Realtek audio, Bluetooth e altri componenti.

## Nuovo flusso della scansione

Il servizio apre **una sola volta** un set di dispositivi
**presenti** tramite `SetupDiGetClassDevsW(DIGCF_PRESENT|DIGCF_ALLCLASSES)`.

Per ogni istanza già identificata da `Win32_PnPEntity.PNPDeviceID`,
solo se manca almeno una categoria di identificativi WMI **validi**, usa:

- `SetupDiOpenDeviceInfoW` con l'**Instance ID esatto**:
  nessuna ricerca per somiglianza di nome o produttore;
- `SetupDiGetDeviceRegistryPropertyW` per
  `SPDRP_HARDWAREID` (1) e/o `SPDRP_COMPATIBLEIDS` (2);
- decodifica dei dati `REG_MULTI_SZ` UTF-16 LE in sola lettura.

L'ordine è **WMI PnPEntity → SetupAPI nativa →
WMI PnPSignedDriver**. Non si sovrascrive un elenco WMI
già presente; il vecchio fallback singolo SignedDriver
rimane disponibile se SetupAPI non restituisce dati.

## Correzione #242 — WMI non vuoto ma privo di ID utilizzabili

Fino alla v0.0.115 l'inventario considerava sufficiente che
`Win32_PnPEntity.HardwareID` o `CompatibleID` restituisse
**almeno un elemento** per disattivare il recupero SetupAPI.
Una stringa non valida, un percorso, un Instance ID con due
barre inverse o un carattere bidi contavano quindi come
«identità già presente», anche se nessun ID era utilizzabile.
Questo poteva nascondere associazioni WUA NVIDIA/Killer/Realtek
già restituite da Windows Update.

Ora `PnpSetupApiIdentity.SanitizeWmiIds` convalida
**ogni identificativo WMI**, rimuove duplicati
case-insensitive e mantiene al massimo **128 ID** validi
nell'ordine originale. Una categoria con soli valori
malformati o oltre il budget viene trattata come
**non verificata**, non come presente: il servizio può
tentare il recupero nativo read-only tramite l'esatto
`PNPDeviceID` di quel dispositivo e, se necessario,
il fallback SignedDriver associato alla medesima istanza.

Se WMI restituisce ID validi, la sua precedenza
su SetupAPI e SignedDriver rimane invariata: nessun
ID Killer di un altro dispositivo può essere assegnato
alla GPU NVIDIA. La convalida è applicata sia prima
dell'interrogazione nativa sia all'interno della funzione
`PnpSetupApiIdentity.Merge`, rendendo la regola testabile
e utilizzabile da tutti i chiamanti.

La modifica aumenta **solo** le possibilità di
correlazione hardware con aggiornamenti WUA già esistenti;
non aggiunge GUID di update, query OEM o permessi di
installazione. Le prove automatiche includono ID NVIDIA,
Killer e Realtek recuperati dopo stringhe WMI malformate,
liste duplicate, overflow 128, priorità WMI valida e
report senza percorsi/seriali. QA sul PC reale
Recoil 16 e benchmark 2-vs-8 ancora aperti (#91, #136).

## Limiti, isolamento e sicurezza

- Massimo **2048** istanze controllate da SetupAPI per scansione,
  massimo **64 KiB** per proprietà, **128** ID per elenco,
  **512 caratteri** per ID. Encoding invalido, struttura
  REG_MULTI_SZ incompleta, tipo errato, duplicati e ID con
  percorsi/command string vengono rifiutati o ignorati.
- Errori API o driver non presente non interrompono la
  scansione WMI valida; il risultato sconosciuto NON significa
  hardware assente. Nessuna modifica al registro o ai driver.
- Si usano solo API `SetupDiGet*`/`SetupDiOpen*`:
  non `SetupDiSet*`, non driver staging, installazione,
  download, EXE esterni o endpoint OEM.
- Il report di copertura copiabile aggiunge solamente
  il **numero aggregato di dispositivi recuperati**;
  i Device Instance ID, seriali e Hardware ID non vengono
  inseriti nei log né nel report copiabile.
- Il nuovo campo di risposta IPC
  `DriverInfo.PnpIdsRecoveredFromSetupApi` è booleano
  facoltativo con default `false` e compatibile con
  i client precedenti.

## Risultato e limiti della copertura

Aumenta la **copertura di rilevamento hardware e matching**
dei driver effettivamente **già offerti da Windows Update**.
Non interroga un nuovo catalogo NVIDIA online, non crea un
aggiornamento quando WUA non ha trovato il relativo GUID e
non rende un risultato di tipo OEM/Software installabile.
Le policy `QuickUpdatePolicy` e la verifica al momento
dell'installazione rimangono invariate.

Per ottenere una misura reale sul Recoil 16:

1. Confrontare il conteggio e l'elenco di componenti
   riconosciuti prima/dopo la scansione.
2. Aprire il report «Copia copertura» e verificare
   quanti ID sono stati integrati **da SetupAPI**.
3. Controllare la matrice fissa NVIDIA GPU / HD Audio /
   Platform, Killer Wi-Fi, Intel e i GUID WUA correlati.
4. Confrontare con Driver Easy senza interpretare
   le semplici associazioni come aggiornamenti confermati.

La differenza 2-vs-8 resta aperta (#91, #136).
La lettura della proprietà PnP locale è documentata da
[Microsoft SetupDiGetDeviceRegistryPropertyW](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdeviceregistrypropertyw).

## Test automatici

- Decoder `REG_MULTI_SZ` con NVIDIA/Intel Killer/Realtek,
  duplicati e ordinamento, typo REG_SZ, lunghezza dispari,
  terminazione mancante, byte extra, limiti e surrogate UTF-16
  malformati.
- Precedenza fra fonti di Hardware ID e Compatible ID.
- Matching a GUID WUA con un nome GPU generico.
- Report aggregato privo di identificativi, compatibilità
  JSON/IPC del campo opzionale.
- Test Windows runner degli export read-only di SetupAPI,
  compilazione servizio/Desktop e gate UI/IPC.

Non costituisce prova di esecuzione sulla GPU NVIDIA
del Recoil 16: la QA hardware rimane necessaria.
