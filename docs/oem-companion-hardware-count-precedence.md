# OmegaDrive — contatori OEM companion coerenti con il PnP (#355)

## Disallineamento

La schermata Home e l'analisi degli otto dispositivi campione
applicano già \`PnpKnownVendorIdentity\` per distinguere il vero
produttore PnP dal nome o provider di un driver installato in precedenza.

La scheda **Provider OEM / companion**, tuttavia, interrogava i
contatori originali di NVIDIA/Intel/AMD. Questi usavano direttamente
\`IsNvidiaDevice\`, \`IsIntelDevice\`, \`IsAmdDevice\`: un codec
\`HDAUDIO\FUNC_01&VEN_10EC&DEV_0295\` con provider installato
«NVIDIA» poteva diventare un falso dispositivo NVIDIA; e un
chipset AMD \`PCI\VEN_1022&DEV_15E3\` con provider Microsoft
poteva non essere riconosciuto come hardware AMD.

Il conteggio originale non deduplicava DeviceID provenienti da
più record WMI della stessa istanza.

## Correzione

\`OemCompanionDeviceCoverage.Count\` è la regola condivisa usata
dai tre provider:

- Raggruppa per **DeviceId non vuoto**, case insensitive: una sola
  istanza = un dispositivo contato.
- Modelli PnP specifici e validati su HardwareID prevalgono sui
  CompatibleID, a loro volta su nome/manufacturer/provider
  installati. Riconosce NVIDIA (GPU PCI, HD Audio, ACPI, DCH SWC,
  USB NVIDIA), Intel/Killer (PCI, SWC, USB) e AMD (PCI/SWC
  VEN_1002 e VEN_1022).
- Modelli con vendor riconosciuto differente o HardwareID in
  conflitto vengono esclusi dal contatore del vendor sbagliato.
  Anche due record per lo **stesso DeviceID** che dichiarano vendor
  incompatibili sono considerati inconcludenti, indipendentemente
  dall'ordine WMI.
- Il fallback sul provider installato resta possibile solo
  quando manca un modello vendorizzato verificato. Un PCI
  \`VEN+DEV\` di un vendor *non riconosciuto* prevale comunque
  sul testo pubblicitario non attendibile.
- Nessun DeviceID, seriale, HardwareID, installazione locale o
  nome privato viene copiato nei conteggi, messaggi al Desktop
  o log dei provider.

Il conteggio stabilisce soltanto **a quale produttore indirizzare
l'utente per un'eventuale verifica manuale**. Non garantisce
che un companion sia installato, che il produttore disponga di
aggiornamenti, che un file sia compatibile con Windows o che
l'aggiornamento sia installabile. Lo stato dei companion software
resta distinto e continua a essere rilevato in sola lettura.

## Regressioni

Test xUnit Core verificano provider obsoleti, caso Realtek/Qualcomm,
modelli NVIDIA/Intel/Killer/AMD riconosciuti con nome Microsoft,
DeviceID duplicati o in conflitto, fallback sconosciuto e
inventari Intel grandi. Test Service istanziano i tre
\`IOemDriverProvider\` concreti, controllano
\`DetectedDeviceCount\`/\`IsApplicable\` e protezione dati
nei messaggi, senza assumere quale companion software sia
presente sul runner CI.

La suite Windows automatizzata NON sostituisce la verifica dei driver
sul PCSpecialist Recoil 16. Il confronto tra otto voci Driver Easy
e le offerte WUA/OEM realmente disponibili rimane aperto (#91/#136).
