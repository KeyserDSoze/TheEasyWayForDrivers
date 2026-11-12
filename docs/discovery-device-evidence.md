# Discovery+ — risultati per singolo dispositivo (incremento #38)

La vista **Discovery+** ora prevede due modalità:
- **Tutte le fonti**: mostra risultati WUA cercati e metadati OEM che l'utente ha consultato durante la sessione corrente.
- **Solo candidati Windows Update del dispositivo selezionato**: selezionare prima un dispositivo nella scheda *Driver installati*, poi abilitare il filtro in Discovery+. Rimangono soltanto candidati WUA il cui Hardware ID coincide esattamente con un Hardware ID o Compatible ID dell'inventario del dispositivo.

## Correzione v0.0.89: entry dispositivo WUA multiple (#186)

La vista **Discovery+** usa gli stessi identificativi del motore WUA e della Home:
`DriverUpdateInfo.HardwareId` **più**
`AdditionalHardwareIds` da `IWindowsDriverUpdate4.WindowsDriverUpdateEntries`.
Il filtro «Solo candidati ...» controlla ora tutte le entry lette, fino al cap
condiviso di 1024 ID aggiuntivi per update (fino a 2048 record WUA COM esaminati); prima leggeva soltanto l'ID
principale. Un unico GUID WUA resta **un** candidato anche se identifica
NVIDIA GPU/HD Audio, Intel Bluetooth o Killer Wi-Fi in entry differenti.

Se `HardwareId` è assente ma esistono entry aggiuntive, Discovery+ può
mostrare la corrispondenza. `AdditionalHardwareIdsIncomplete` viene
mantenuto nell'adapter e mostrato nel testo: un mancato match in un
elenco WUA incompleto **non prova assenza** di offerte per il dispositivo.
I pacchetti catalogo Dell/HP/Lenovo restano fuori dal filtro per ID.

I test verificano associazioni GPU/audio NVIDIA, Killer oltre la entry
128, ID principale mancante, priorità Hardware/Compatible ID, limiti,
near-miss e separazione OEM/WUA. L'intervento **non** interroga nuove
sorgenti, non aumenta il numero di GUID WUA ricevuti e non permette di
installare driver firmware, optional/advanced/hidden o Software. I gate
`QuickUpdatePolicy` sul client e nel servizio restano invariati.

QA reale Recoil 16: confrontare il filtro Discovery+ con l'elenco
per-dispositivo della Home dopo una scansione Comprehensive; i due
devono restituire le stesse correlazioni WUA per ID e il dettaglio deve
segnalare i casi con entry incomplete. Non pubblicare seriali né
identificativi di istanza PnP nei report. Rif. #91 #136 #176 #178 #186.

---

La nuova classe `DiscoveryDeviceEvidence` restituisce un'evidenza numerica: **2** = Hardware ID esatto, **1** = Compatible ID, **0** = nessuna associazione verificata. Un identificativo corrispondente non certifica l'installazione sicura.

**Dell Driver Pack e HP SoftPaq non vengono assegnati automaticamente al singolo dispositivo**, anche se nel catalogo compaiono nomi somiglianti: servirebbe prova di compatibilità tramite ID hardware/INF e OS, non ancora presente nei relativi metadati normalizzati. Lenovo rimane consultabile nella scheda OEM in attesa di dati e schema reale verificati.

Una **nuova scansione hardware cancella i risultati WUA e OEM mantenuti in memoria**, perché rappresentano la ricerca associata all'inventario precedente. I pacchetti OEM non vengono scaricati né installati automaticamente. Tutto il filtro è locale.

Questa fase non modifica il servizio privilegiato o il flusso d'installazione. Le prove runtime su PC Windows 11 sono tracciate in #5. Refs #38, #34, #36.


## Privacy della descrizione Hardware ID nei dettagli dispositivo (#409, 12 novembre 2026)

Il riepilogo locale `DriverDeviceRow.HardwareIdentityHint` usa `HardwareIdentityParser.Describe`. Non è una trascrizione dell'Hardware ID: per bus ROOT, SWD, USBSTOR, HID, audio, monitor, Bluetooth, stampanti e altri mostra **soltanto la categoria**, senza ripetere il token PnP grezzo che può includere un seriale, dato OEM privato o numero d'ordine. Per ACPI vengono mostrati soltanto codici di modello pubblici riconosciuti (PNP con quattro cifre esadecimali e NVDA con quattro cifre esadecimali); gli altri restano descrizioni di categoria. Per PCI e USB, VEN/DEV o VID/PID vengono estratti solo dopo la verifica dell'**intero modello pubblico** con la stessa grammatica prudente della ricerca manuale Catalog: un identificativo di istanza con ulteriore componente, un campo sconosciuto o un modello incompleto non può produrre un riepilogo apparentemente validato.

Gli ID grezzi eventualmente visibili nell'inventario avanzato restano dati locali da trattare con cautela quando si condividono screenshot; il riepilogo riassuntivo non li duplica. La descrizione non costituisce verifica di compatibilità, firma CAT, disponibilità di un nuovo driver né autorizzazione all'installazione. Test sintetici su NVIDIA/Intel, Realtek USB, ACPI PNP/NVDA, HID, USBSTOR, ROOT/SWD, stampanti, seriali, suffissi d'istanza e input misformati.
