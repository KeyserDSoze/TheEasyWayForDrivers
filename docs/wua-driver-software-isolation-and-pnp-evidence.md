# OmegaDrive · stato WUA driver vs software e componenti PnP generici (#144, #145)

## Difetto corretto

Una scansione approfondita usa pass distinti per `Type='Driver'` e,
solo come indizio, `Type='Software'` (Intel/Killer/NVIDIA software/extension).
La proprietà `DriverSearchReport.IsPartial` includeva erroneamente
`SoftwareSearchIsPartial`: un errore durante l'indagine **non installabile**
poteva disabilitare `Aggiorna tutto` persino quando tutti i pass driver
erano completati.

Ora la copertura driver per l'installazione è valutata solo da
`DriverSearchReport.Sources`. La ricerca Type=Software conserva il proprio
flag `SoftwareSearchIsPartial` e uno stato visibile distinto nella Home
e nel report diagnostico aggregato.

| Ricerca Type=Driver | Ricerca Type=Software | Interpretazione |
| --- | --- | --- |
| Completa | Completa | Candidati WUA ordinari eleggibili secondo policy; software solo informativo |
| Completa | Parziale/fallita | Driver WUA completi; indicare software incompleto senza bloccare il batch driver |
| Parziale/fallita | Completa | Driver incompleti; `Aggiorna tutto` disabilitato |
| Fallita | Fallita | Nessun esito conclusivo su disponibilità driver |

Il pass WUA `NotRegistered` non è un tentativo fallito. Non registriamo
servizi Microsoft Update o alteriamo la configurazione del PC.

**Importante:** il fatto che tutti i pass *configurati* WUA siano riusciti
non significa che ogni vendor OEM sia stato interrogato o che ogni driver
esistente sia già aggiornato.

## Componenti generici nell'evidenza diagnostica

`VendorDriverEvidenceFinder` ora riconosce componenti con nomi generici
come `Network Controller` e `Generic radio device` se:

- Intel: Hardware/Compatible ID associati al produttore e classe Windows
  `Net`, `Bluetooth` oppure `Display`;
- NVIDIA: Hardware ID NVIDIA e classi `Display`, `MEDIA`, `System`
  o `SoftwareComponent`;
- AMD: Hardware ID AMD e classe `Display`.

La logica evita di promuovere un generico bridge Intel (`System`) a
driver da aggiornare e rifiuta falsi prefissi PnP come `PCI\\VEN_80860`.
L'evidenza riporta soltanto la versione installata e i **candidati WUA
correlati per ID**. La loro compatibilità resta da verificare, l'origine OEM
non è interrogata e nessuna evidenza diagnostica entra in `DriverUpdates`.

## Validazione

- `DriverSearchReportTests`: driver completi/software fallito, casi
  inversi e sorgente Software non registrata.
- `WuaSourceFailurePolicyTests`, `SimpleScanPresentationTests` e
  `ScanCoverageTextTests`: messaggi corretti, stato separato, nessun
  path privato nel report.
- `VendorDriverEvidenceTests`: Intel WLAN/Bluetooth generici con ID
  reale o compatibile, correlazione WUA, Intel bridge e falso prefisso.
- Pipeline Windows PR: IPC, contratto UI, build .NET 10, xUnit.

## Resta aperto

Il confronto 2 vs circa 8 candidati (#91 e #136) richiede la misura reale
sul PC e pacchetti/sorgenti ufficiali verificabili. **Questa correzione
non dichiara di trovare sei nuovi driver**. Nessun driver di terze parti
viene scaricato o installato automaticamente.
