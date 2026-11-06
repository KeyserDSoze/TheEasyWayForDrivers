# OmegaDrive — ricerca manuale nel Microsoft Update Catalog per dispositivo (#172)

## Perché è utile

La ricerca automatica di OmegaDrive interroga Windows Update Agent (WUA), con i pass già configurati e le fonti Microsoft Update registrate. **WUA non restituisce necessariamente ogni driver pubblicato nel Microsoft Update Catalog**: il catalogo è una superficie pubblica consultabile manualmente per modello, produttore, classe e Hardware ID. Perciò “non offerto da WUA” non equivale a “nessun pacchetto esiste”.

Microsoft documenta espressamente la ricerca per Hardware ID:
- https://www.catalog.update.microsoft.com/Faq.aspx
- https://learn.microsoft.com/en-us/windows-server/administration/windows-server-update-services/manage/wsus-and-the-catalog-site

Non è stata identificata una API ufficiale pubblica del Catalogo, documentata per download/ricerca driver per-device, da integrare senza scraping. OmegaDrive **non** interroga il sito automaticamente.

## Nuovo flusso

1. Eseguire **Analizza il PC** e selezionare un dispositivo nella scheda **Driver installati**; usare “**Cerca nel Catalogo Microsoft**”. In alternativa, aprire il pannello avanzato già collassato **Produttori e fonti ufficiali → Driver già presenti in Windows · ranking locale**, scegliere un dispositivo e premere “**Cerca pacchetti nel Catalogo Microsoft**”.
2. Il codice Core `MicrosoftUpdateCatalogSearch.Create` legge soltanto gli array `HardwareIds`, con fallback su `CompatibleIds` **se nessun Hardware ID strutturato è presente**. Supporta identificativi modello a formato sicuro `PCI`, `USB`, `HDAUDIO`, `INTELAUDIO`, `SWC` e l'ACPI NVIDIA `ACPI\\NVDA####`.
3. Da ciascun ID viene ricavata la sola **famiglia di modello** necessaria alla ricerca: `VEN/DEV`, `VID/PID`, `FUNC/VEN/DEV`, senza `SUBSYS`, `REV`, `MI`, seriali, `DeviceId` di istanza, modello completo del PC, nome dispositivo o informazione d'utente.
4. Se nessun ID supera la validazione **fail-closed**, il browser non viene aperto. Non viene tentato alcun matching per stringa nome o per titolo dell'aggiornamento.
5. Prima di aprire il browser, **un dialogo di conferma mostra esattamente la query** che Microsoft riceverebbe e avvisa che il catalogo può mostrare più PC/vendor. Dopo consenso, viene aperto il solo URL fisso `https://www.catalog.update.microsoft.com/Search.aspx?q=...` (HTTPS e host controllati).
6. L'apertura è manuale: nessun download avviato da OmegaDrive, modifica dei servizi Windows Update, nuova offerta in `DriverUpdateInfo` o entry in `Aggiorna tutto`.

## Avvertenze sulla compatibilità

- Una query senza `SUBSYS` o identificativi OEM precisi è **intenzionalmente ampia** per proteggere la privacy; risultati del Catalogo per la stessa coppia `VEN/DEV` possono essere destinati a notebook diversi.
- Se è stato necessario usare un **Compatible ID**, il dialogo avvisa che il livello di evidenza è ancora più debole.
- Una voce del catalogo non prova che il pacchetto sia più nuovo, necessario, adatto al modello, alla versione/build Windows o all'architettura, né che passi firma, CAT, dipendenze, rank SetupAPI e policy OEM.
- Catalogo e Microsoft Update (WUA) restano concetti diversi: **il numero di risultati sul sito NON entra nel conteggio degli aggiornamenti automaticamente installabili**.
- Per PCSpecialist Recoil 16, priorità al sito OEM notebook e agli strumenti ufficiali dei vendor prima di valutare manualmente driver generici.

## Test e QA

- [x] Core: ricerca modello per NVIDIA PCI, Intel/Killer PCI, Realtek HDAUDIO/INTELAUDIO, periferica USB, SWC NVIDIA e ACPI NVIDIA Platform.
- [x] Priorità Hardware ID vs Compatible ID; fallback solo se non è disponibile un ID strutturato, evidenza più debole indicata.
- [x] URL HTTPS ufficiale, singolo parametro `q` URL encoded, nessun device title, Instance ID, serial, SUBSYS, revision, ecc.
- [x] ID malformati, vendor/device near-miss, path traversal, CR/LF, Unicode bidi, token di controllo o seriali = nessun URL.
- [x] UI: bottoni nel pannello avanzato Home e scheda per dispositivo; consenso prima dell'apertura, separazione dall'installazione, contratto statico WPF.
- [ ] CI Windows .NET 10, test Core/Service, compilazione WPF/IPC e nuova release.
- [ ] QA reale #136: consultare il catalogo per NVIDIA RTX 4060, NVIDIA HD Audio/Platform, Intel UHD/Bluetooth/Killer Wi-Fi; confrontare le otto voci Driver Easy e verificare *manualmente* compatibilità OEM/CHID/SUBSYS, versione OS, data/versione INF, firma CAT e rank prima di concludere.

**Limite esplicito:** questa feature può portare l'utente a trovare pacchetti **pubblicati** oltre ciò che WUA offre al PC, ma non implementa una ricerca automatica OEM/cross-vendor e non garantisce di colmare il divario 2 vs 8. Restano aperti #91, #100, #130, #136.

Issue #172.
