# WUA: Hardware ID in fondo a collezioni molto grandi — issue #256

## Difetto nella v0.0.122

Una singola offerta `Type=Driver` di Windows Update può descrivere
molte `IWindowsDriverUpdateEntry`. OmegaDrive ne ispezionava
correttamente fino a **2.048**, ma salvava subito nell'IPC solo i
**primi 1.024 ID Hardware unici**. Le voci successive potevano
contenere identificativi esatti NVIDIA GPU, Intel/Killer Wi-Fi o
Realtek Audio: una corrispondenza hardware effettivamente riportata
da Windows veniva dunque persa anche se il GUID WUA era presente.

La scansione precedente impostava correttamente il flag
`AdditionalHardwareIdsIncomplete`, ma non poteva mostrare
l'associazione al dispositivo se l'ID era già stato scartato.

## Selezione bilanciata, senza aumentare i dati trasferiti

`WindowsDriverEntryIdReader` continua a leggere un massimo di
**2.048 entry WUA** e valida ciascun identificativo PnP prima
di acquisirlo. Poi `WuaDriverEntryIdSelection` produce **al
massimo 1.024 ID per GUID** come nella versione precedente.

- Con **1.024 o meno ID unici**, il risultato rimane invariato,
  nello stesso ordine COM.
- Con **più di 1.024**, mantiene intatti i **primi 960** ID
  e assegna i **64 posti restanti** a turno fra i gruppi
  di ID pubblici PCI/USB/audio **NVIDIA, Intel/Killer,
  Realtek, AMD, Qualcomm/Atheros, Broadcom, MediaTek**.
- Se ci sono pochi ID di queste famiglie, gli slot residui
  vengono riempiti in ordine originale con altri ID WUA.
  Non si fabbricano identificativi, non si usa il nome del
  driver o del dispositivo, non si promuovono stringhe
  imprecise o simili ai codici hardware.
- Ogni GUID WUA rimane **una sola offerta**; la selezione
  aumenta esclusivamente la possibilità di associare
  quel GUID a un dispositivo effettivo. Se un ID non è
  conservato o una COM entry fallisce, l'elenco resta
  **esplicitamente incompleto**.

Nessuna di queste associazioni, anche con Hardware ID esatto,
dimostra che la versione sia più nuova, compatibile col firmware
OEM, scelta da SetupAPI, WHQL o installabile.

## QA

I nuovi test verificano ID NVIDIA, Killer e Realtek
**dopo il 1.024° elemento**, prova con tutte e sette le
famiglie, centinaia di ID NVIDIA che non devono oscurare
Realtek, array fino a 2.048 entry, duplicati e valori
invalidi, saturazione dei 1.024 slot, lettura incompleta,
matching WUA/PnP e assenza di ID o dati personali
nei report esistenti.

Il budget di **2.048 letture COM / 1.024 ID IPC** non viene
aumentato; non vengono introdotte nuove fonti online,
download, staging, installazione o modifica del pulsante
`Aggiorna tutto`.

Sul PCSpecialist Recoil 16 resta da verificare se gli
aggiornamenti WUA hanno davvero collezioni estese e se
gli ID recuperati corrispondono a dispositivi reali.
Il gap **2 vs 8** con Driver Easy rimane aperto
nelle issue #91 e #136.

## Estensione DCH / ACPI (#268)

La selezione rappresentativa delle offerte WUA con **più di 1.024 ID**
copre ora anche identificativi di componenti Windows già riconosciuti
dall'inventario PnP ma precedentemente non selezionati nei 64 slot
riservati:

- NVIDIA Platform Controller: `ACPI\NVDA####` con quattro cifre
  esadecimali e confine esatto (nessuna accettazione di `NVDA0820X`),
  oltre ai SoftwareComponent `SWC\VEN_10DE&...`.
- Componenti DCH e audio Intel: `SWC\VEN_8086&...`,
  `INTELAUDIO\FUNC_01&VEN_8086&...` e
  `HDAUDIO\FUNC_01&VEN_8086&...`.
- Componenti SWC Realtek: `SWC\VEN_10EC&...` (oltre al precedente
  `INTELAUDIO\FUNC_01&VEN_10EC&...`).
- USB Qualcomm: `USB\VID_05C6&...`.

Queste stringhe sono **esclusivamente ID restituiti dalla fonte
WUA**. Un token riconosciuto serve solo a dare priorità a un
identificativo già letto entro l'IPC, non a fabbricare driver,
autorizzarne l'installazione o cambiare l'inventario Windows.
`DriverUpdateMatcher` richiede poi uguaglianza esatta con l'ID
dispositivo. Per gli insiemi non più grandi di 1.024, il risultato
rimane immutato; cap COM 2.048, cap IPC 1.024, 64 slot e
segnalazione di copertura incompleta non cambiano.

I test verificano i casi reali sintetici DCH/ACPI a fine elenco,
falsi prefissi, lettura COM, merge di due fonti e il conteggio di
un solo GUID anche quando più dispositivi sono associati.

**Nessuna prova di aggiornamenti addizionali sul Recoil 16**:
la copertura reale e il confronto 2/8 vanno ancora misurati
sul dispositivo in #91/#136.
