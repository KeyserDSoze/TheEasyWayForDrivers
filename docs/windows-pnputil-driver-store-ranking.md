# Ranking driver già presenti in Windows con PnPUtil — issue #132

## Perché questo sviluppo

Nel PC **PCSpecialist Recoil 16**, OmegaDrive ha finora potuto
mostrare ~2 aggiornamenti Windows Update mentre un altro software
indicava ~8 driver (NVIDIA GPU, audio e componenti platform,
Intel/Killer ecc.). Parte del divario può derivare dal fatto che:

- **Windows Update** espone gli aggiornamenti pubblicati per il PC,
  non ogni driver disponibile nel mondo;
- il **Driver Store** contiene più versioni/pacchetti già presenti
  o memorizzati sul PC, alcuni dei quali possono corrispondere allo
  stesso dispositivo, ma **non sono necessariamente aggiornamenti**;
- il catalogo OEM può contenere pacchetti esterni non ancora nel
  Driver Store e quindi non classificati da Windows.
- un DriverVer numericamente superiore NON prevale sempre: nella
  selezione Windows contano prima firma, feature e matching
  Hardware ID/Compatible ID, e a parità di rank la data/versione.

Microsoft documenta la consultazione dei driver associati a
un dispositivo e del rank tramite:

\`%SystemRoot%\System32\pnputil.exe /enum-devices /instanceid <ID dispositivo> /drivers\`

Fonti:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-windows-ranks-driver-packages
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-windows-selects-drivers

## Come provarla

1. Esegui **Analizza il PC** in OmegaDrive.
2. Espandi **Driver già presenti in Windows · ranking locale**.
   La sezione è nascosta fino alla disponibilità di un inventario
   e chiusa per default per non complicare la Home.
3. Seleziona **NVIDIA GeForce RTX 4060 Laptop GPU**, poi
   **Mostra scelta Windows**. È possibile scegliere anche
   NVIDIA Audio, Intel/Killer o un altro dispositivo PnP.
4. L'app avvia soltanto **pnputil.exe di Windows** in modalità
   elenco driver per **quel singolo dispositivo**; l'output
   locale viene mostrato in una casella di testo read-only
   con scroll, senza salvare il contenuto nei report copiabili.
5. Confronta l'eventuale rank mostrato direttamente da Windows
   con le versioni già installate. I numeri (quando presenti)
   sono quelli restituiti da Windows, **non ricostruiti** da
   OmegaDrive. L'output può essere in italiano o inglese.

## Sicurezza, affidabilità, privacy

- Argomenti fixed allowlist: \`/enum-devices\`,
  \`/instanceid\`, ID da inventario reale, \`/drivers\`.
  Mai \`/add-driver\`, \`/install\`, \`/delete-driver\`,
  \`/scan-devices\`, \`/restart-device\`, installatori OEM
  né shell \`cmd.exe\`.
- Il percorso dell'eseguibile è costruito con
  \`Environment.SystemDirectory\`; verifica che sia un
  file reale, non reparse point. Non usa PATH/working dir
  controllate da terzi.
- \`ProcessStartInfo.ArgumentList\`, \`UseShellExecute=false\`.
- Timeout 25 secondi, cancellazione/kill del processo.
- Massimo 48 KiB per stream stdout/stderr; output e errori
  troppo grandi invalidano il risultato anziché produrre
  una lista troncata spacciata come completa.
- Instance ID e ID hardware/compatible del device
  vengono oscurati prima di mostrare la risposta, e non
  sono inclusi nel report aggregato, log o telemetria.
  Lo stderr grezzo non viene mostrato.
- L'output di PnPUtil è **testo localizzato** e può
  variare tra release Windows; questa fase NON lo usa
  per calcolare automaticamente un rank o una decisione
  di update. Nessun valore viene inferito dall'INF
  locale non ancora staged.
- Se la query non riesce, l'esito è «non verificabile»,
  mai «nessun aggiornamento» o «tutti aggiornati».
- Il risultato non viene aggiunto alla lista Windows
  Update, alle sorgenti OEM assessed o ad **Aggiorna tutto**.

## Test automatici

- [x] La command policy accetta soltanto l'ID del device
  reale e i 3 flag read-only consentiti.
- [x] Rifiuto di instance ID vuoto, troppo lungo, newline,
  quote/control che potrebbero alterare gli argomenti.
- [x] Sanitizzazione di Instance ID, Hardware ID, Compatible
  ID e caratteri di controllo/bidi nel testo PnPUtil.
- [x] Rank hexadecimal stampato da Windows resta visibile
  senza essere interpretato o trasformato in update.
- [x] Output in lingua italiana/inglese, uscita non zero,
  assenza output e output oltre 48 KiB gestiti fail-closed.
- [x] Contratto WPF: sezione nascosta fino a inventario,
  compressa all'avvio, risultato read-only e selezione
  legata all'inventario \`Drivers\`.
- [ ] CI Windows: compilazione e test.
- [ ] Collaudo reale Recoil 16 con NVIDIA GPU/HD Audio e
  Intel/Killer; il ranking mostrato può dipendere dal
  sistema e dalla sua configurazione.
- [ ] Confronto con pacchetti OEM ufficiali e gli ~8
  driver proposti da Driver Easy: ticket #91.

## Non incluso in questo incremento

- Nuovi endpoint cataloghi NVIDIA, Intel o PCSpecialist.
- Confronto automatico online o promozione dei driver
  già nel Driver Store a un aggiornamento da installare.
- Ranking per pacchetti esterni non staged: non possiamo
  attribuire loro un rank Windows vero senza un controllo
  del sistema che lo supporti.

Questo lavoro rende **osservabile la scelta reale di Windows**,
ma non garantisce più driver installabili o aggiornamenti online.

Riferimenti #132 #130 #91 #100.
