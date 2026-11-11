# OmegaDrive — modelli audio SNDW/ACP oltre 1.024 ID WUA (#356)

## Lacuna dopo #353/#354

OmegaDrive verifica e correla modelli SoundWire (SNDW) e AMD ACP
completi ricevuti in \`WindowsDriverUpdateEntries\`, ma il trasporto
Desktop/Service ammette al massimo **1.024 ulteriori Hardware ID**
per GUID WUA. Quando WUA restituisce più entry, il servizio usa
\`WuaDriverEntryIdSelection\` con **64 slot vendor-fair**: serve a
salvare ID NVIDIA/Killer/Realtek e vendor rari dopo un lungo elenco
di dispositivi comuni.

I modelli SNDW e ACP non sono inclusi nei formati di ricerca web
del Catalog pubblico \`MicrosoftUpdateCatalogSearch\` (correttamente:
il catalogo web non è una prova di compatibilità). Il selettore
usava questo parser e, in fallback, cercava solo \`&DEV_\` o
\`&PID_\` per ricavare una famiglia. Un ID
\`SNDW\CLASS_00&MAN_025D&PART_0711&VER_02&UID_00\` o
\`ACP\DEVTYPE_0005&DEVREV_0000&VEN_1022&DEV_15E2\`
finito dopo i primi 1.024 perdeva quindi la priorità.
Poteva essere **dichiarato da WUA** ma non apparire come
associazione al codec PnP.

## Intervento

Solo nel selettore delle **entry WUA validate**,
\`GetSpecificModelFamily\` riutilizza ora
\`ModernAudioPnpMatchPolicy.IsSpecificHardwareId\`, introdotto
con #354, e ricava famiglie:

- SNDW: \`SNDW\CLASS_XX&MAN_YYYY\`, indipendente da PART,
  VER e UID;
- ACP: \`ACP\DEVTYPE_XXXX&DEVREV_YYYY&VEN_ZZZZ\`,
  indipendente dall'ultimo DEV del modello.

Vengono preservati per primi i modelli di famiglie non ancora
rappresentate, poi gli altri della stessa famiglia, all'interno
degli **stessi 64 slot** dedicati ai vendor rari. I gruppi
NVIDIA/Killer/Realtek/AMD/Qualcomm/Broadcom/MediaTek e
SoundWire SDCA esistenti sono invariati. **Nessun aumento** del
budget COM, degli ID IPC o delle ricerche in rete.

ID come \`SNDW\CLASS_00\`, \`SNDW\CLASS_00&MAN_025D\`,
\`ACP\DEVTYPE_0005\` o \`ACP\DEVTYPE_0005&DEVREV_0000\`
restano insufficienti per un match WUA/INF e non ricevono
priorità; sintassi invalide, suffix inventati o seriali sono
respinti prima di poter consumare uno slot protetto.

## Sicurezza e significato

Il risultato è unicamente l'associazione **esatta** di un
Hardware ID restituito dal COM Windows Update a un dispositivo
PnP già rilevato da Windows. Un GUID WUA che dichiara 50
dispositivi rimane **un solo aggiornamento GUID**; non vengono
inventati nuovi pacchetti, download o versioni.
\`AdditionalHardwareIdsIncomplete\` rimane true quando non sono
stati conservati tutti gli ID, anche se quelli audio sono
sopravvissuti. Le policy Quick Update, firmware, browse-only,
install-time recheck e firma non vengono cambiate.

Le regressioni verificano Core, reader COM simulato,
dataset di oltre 1.024 e oltre 2.560 entry, merge di due
fonti WUA con lo stesso GUID, diversità fra famiglie,
assenza di match per classi generiche, ID privati nel
report esportato, budget e diagnostica.

Non è stato ancora dimostrato un aumento sul PCSpecialist
Recoil 16: il confronto 8 candidati Driver Easy vs 2
visibili in OmegaDrive rimane da verificare sul dispositivo
reale (#91, #136, #85).
