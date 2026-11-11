# WUA SoundWire SDCA: equità fra famiglie codec (#366)

## Problema

Una sorgente Type=Driver WUA può dichiarare centinaia di entry audio
\`SOUNDWIRE\SDCA_06&MAN_025D&FUNC_XXXX\` e poi un altro codec
\`SOUNDWIRE\SDCA_07&MAN_0242&FUNC_0811\`. Oltre il limite
IPC di 1.024 ID, i 64 slot riservati erano distribuiti
per gruppo vendor/bus, ma dentro il gruppo SDCA (#7) il primo
produttore poteva assorbirli tutti. La selezione precedente dava
già precedenza alle diverse famiglie SNDW e ACP, non a quelle SDCA.

## Correzione

Il gruppo #7 ora usa la stessa politica family-first dei gruppi
SNDW/ACP e altri modelli. La famiglia specifica è la chiave pubblica
\`SOUNDWIRE\SDCA_XX&MAN_XXXX\` estratta **solo dopo** la verifica
integrale del modello tramite \`MicrosoftUpdateCatalogSearch.TryGetPublicModelKey\`
e \`DriverUpdateMatcher.IsCorrelatableHardwareId\`.
Un \`FUNC\` successivo della stessa famiglia compete per gli slot
solo dopo che le altre famiglie validate sono state rappresentate.

\`AGGREGATOR\`, ID incompleti, \`UID\` isolati, attributi
non supportati e PnP instance path non sono privilegiati.
L'ID originale SoundWire resta immutato: nessun matching per solo
MAN/SDCA, nessuna nuova installabilità o nuovo GUID.

## Invarianti e verifica

- Primi 960 ID first-seen invariati; 64 slot fair; tetto IPC
  1.024 per GUID; limiti COM invariati.
- Stesso round-robin fra gruppi NVIDIA/Intel/Realtek/AMD,
  Qualcomm/Broadcom/MediaTek, SoundWire, SNDW, ACP e altri.
- Test sintetici: 140–150 varianti FUNC della stessa famiglia,
  un secondo SDCA+MAN tardivo, class/aggregator/instance
  negatives, doppia sorgente WUA e report aggregato senza dati
  PnP privati.

Il gap di copertura reale Driver Easy vs OmegaDrive (#91)
e il test fisico Recoil 16 (#136) **non sono risolti**
da una diversa selezione dei soli ID già dichiarati da WUA.
