# OmegaDrive — ranking Windows locale SetupAPI (issue #200)

## Perché

La diagnostica precedente mostrava il testo localizzato di
`pnputil /enum-devices /instanceid ... /drivers`, senza interpretare
il numero `Rank`. Per valutare il comportamento **locale di Windows**
in modo indipendente dalla lingua, la scheda avanzata per un singolo
dispositivo ora interroga anche le API documentate di SetupAPI:

1. `SetupDiGetClassDevsW` (dispositivi presenti, tutte le classi).
2. `SetupDiOpenDeviceInfoW` per l'**Instance ID PnP** scelto nella UI,
   usato solo in memoria e mai esportato nel report.
3. `SetupDiBuildDriverInfoList(SPDIT_COMPATDRIVER)` per i driver
   *già locali*, seguita da `SetupDiEnumDriverInfoW`.
4. `SetupDiGetDriverInstallParamsW`: lettura del **Rank
   assegnato da Windows** al nodo locale, non un punteggio calcolato
   da OmegaDrive.
5. `SetupDiDestroyDriverInfoList` e `SetupDiDestroyDeviceInfoList`
   per liberare le risorse native.

La vista ordina i driver locali per Rank numerico crescente: la
priorità di Windows è generalmente migliore per valori più bassi.
Mostra provider, descrizione e versione `A.B.C.D` recuperata
dall'informazione nativa, non una presunta versione OEM online.

## Uso

Dopo **Analizza il PC**, aprire **Dettagli avanzati → Driver locali ·
rank Windows SetupAPI e PnPUtil**. Selezionare GPU NVIDIA, Intel
Killer, Realtek o un altro dispositivo e scegliere il comando
già disponibile per interrogare Windows. La lettura è **su richiesta**,
non una nuova scansione automatica durante la Home.

Se SetupAPI non è disponibile o la lista non include driver leggibili,
la vista indica **non verificato** e mantiene l'output testuale di
PnPUtil. Se PnPUtil non è disponibile, i rank SetupAPI già letti
possono comunque essere mostrati. Nessun esito vuoto significa
automaticamente «nessun aggiornamento».

## Sicurezza, limiti e differenza rispetto agli OEM

- Le API utilizzate sono letture e costruzione **in memoria** di una
  lista di driver locali. Nessuna chiamata `SetupDiSet*`,
  `SetupDiCallClassInstaller`, `UpdateDriverForPlugAndPlayDevices`,
  `pnputil /add-driver`, `/install` o modifica al Driver Store.
- Al massimo **256 driver node** enumerati e **32 righe mostrate**
  per singolo dispositivo. In caso di errori su un rank o raggiungimento
  del limite, la copertura diventa **parziale**.
- Le etichette ricevute dai driver non sono considerate fidate:
  massima lunghezza 72 caratteri, controlli Unicode/bidi, percorsi,
  Hardware ID e identificatori PnP rimossi dal testo. Il Device
  Instance ID è passato solo alla API in locale.
- La ricerca SetupAPI usa la lista standard di driver compatibili
  nel sistema. Può non includere driver esclusi dalla selezione
  (`ExcludeFromSelect`); non forza questa inclusione con API
  che modifichino parametri di installazione, neppure temporanei.
- Il Rank reale dei **driver già locali** NON assegna un rank agli
  archivi OEM ZIP/CAB/EXE non installati e NON dimostra compatibilità,
  provenienza, firma del CAT o necessità di aggiornare.
- Nessuno dei risultati native/PnPUtil confluisce in `Aggiorna tutto`
  o viene registrato nei report condivisi/log/telemetria.
- La funzionalità non risolve il gap di copertura online **#91**,
  non certifica gli otto aggiornamenti di Driver Easy e richiede
  una prova di runtime sul Recoil 16 (**#136**). Il passo
  successivo di **#130** resta la verifica separata di pacchetti
  OEM esterni via firma/payload/compatibilità, mai per inferenza.

## Test

Unit test sul modello dati: sorting e valori Rank, quattro segmenti
della versione, privacy e scrubbing, overflow 32/256, report parziale,
assenza update/installabilità. Sul runner Windows viene verificata
la presenza delle entry point native SetupAPI richieste. La CI
WPF/IPC verifica che il codice compili insieme al resto dell'app.

Documentazione Microsoft: `SetupDiBuildDriverInfoList`,
`SetupDiEnumDriverInfo`, `SetupDiGetDriverInstallParams` e
`SP_DRVINSTALL_PARAMS.Rank`.
