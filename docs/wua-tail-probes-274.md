# Discovery: WUA entry Hardware ID oltre la posizione 2048 (#274)

## Perché
WUA può restituire una sola offerta Type=Driver (un GUID) con migliaia
di `IWindowsDriverUpdateEntry`: ognuna descrive un Hardware ID dichiarato
da Windows. Prima il lettore `WindowsDriverEntryIdReader` ispezionava
soltanto le prime 2048 entry. Un PnP ID NVIDIA / Killer / Realtek / DCH
nella coda non veniva mai letto; il GUID non appariva come correlato
a quel dispositivo anche se la ricerca Windows Update era completata.

## Come funziona adesso
`WuaDriverEntryReadPlan` crea un piano bounded e deterministico:

- Se Count <= 2560, tutte le posizioni 0..Count-1 sono esaminate.
- Se Count > 2560, le prime **2048** sono tutte conservate; altre
  **512** posizioni vengono campionate uniformemente nell'intervallo
  2048..Count-1. Si include sempre l'ultima entry. Nessun doppione.
- Sono eseguiti al massimo **2560 accessi COM**, anche con Count enorme.
  L'aritmetica per gli indici usa `long`, senza overflow.
- Il limite per il trasporto IPC e la correlazione finale resta
  **1024 ID distinti per GUID**, con la selezione vendor-fair esistente.
- Se un tratto è saltato, se un getter COM fallisce o se il set ID
  supera il cap di trasporto, `AdditionalHardwareIdsIncomplete = true`.

In particolare un ID presente oltre la vecchia barriera 2048 può ora
essere letto e associato a un dispositivo PnP già rilevato.
La probabilità di cattura non è del 100% per liste >2560 e non è
corretto descrivere i risultati come esaustivi.

## Sicurezza
Non si interrogano nuovi cataloghi, non si crea alcun GUID, non si
installa o scarica nulla e non si usa il titolo per inventare ID.
Ogni posizione è letta soltanto dalla collezione WUA già restituita
da una fonte verificata; il filtro valida gli ID e la correlazione
del device usa identità esatta. Non modifica `QuickUpdatePolicy`.
I test validano campionamento, limiti, ID tardivi e regressioni.

Il gap 2 vs 8 del Recoil 16 (#91/#136) necessita comunque del test
sul PC reale e di fonti OEM applicabili: non affermiamo copertura completa.
