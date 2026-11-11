# OmegaDrive — recupero SoundWire SDCA dalla stessa istanza PnP (#353)

## Gap risolto

Il modello WUA SoundWire\SDCA_06&MAN_025D&FUNC_0711, con eventuali
TYPE/VER/ADR/LID/UID/SUBSYS, è già correlabile in OmegaDrive quando
Windows lo fornisce come Hardware ID completo. Tuttavia WMI
`Win32_PnPEntity.HardwareID` può riportare una lista con soli token
SoundWire condivisi o parziali; `PnpSetupApiIdentity.Merge`
prima non provava a recuperare il modello SoundWire dalla stessa
istanza `SetupDiOpenDeviceInfo`, mentre lo faceva per
PCI, USB, HID, HDAUDIO, INTELAUDIO, SWC e USBSTOR.

Un aggregato SoundWire è una classe o endpoint, **non** un codec:
non può da solo diventare un match WUA/INF. La scoperta può però
sfruttare un modello SDCA completo letto via SetupAPI (o
`Win32_PnPSignedDriver`) del **medesimo DeviceID**.

## Nuovo recupero bounded, read-only

Solo i seguenti identificativi WMI consentono di richiedere
metadati opzionali alla stessa istanza:
- `SOUNDWIRE\AGGREGATOR` e `SOUNDWIRE\AGGREGATEDSPEAKER`;
- `SOUNDWIRE\SDCA_XX` (famiglia SDCA con due hex);
- `SOUNDWIRE\SDCA_XX&MAN_YYYY` (stessa famiglia + vendor a quattro hex).

La risposta nativa deve sempre essere un modello SoundWire
**SDCA+MAN+FUNC completo**, validato dalla stessa grammatica del
Catalog pubblico e da `DriverUpdateMatcher.IsCorrelatableHardwareId`.
Se WMI specifica SDCA e/o MAN, tali codici devono coincidere.
Niente modelli da bus PCI/USB/HDAUDIO, vendor/famiglie diversi,
suffix arbitrari, ID di istanza, seriali o un altro dispositivo.

Per hardware WMI già completo, set di WMI con bus o vendor in
conflitto, o token non riconosciuti, non avviene il recupero.
La priorità è sempre **HardwareID WMI completo**, poi modello
SetupAPI per la stessa istanza, poi SignedDriver dello stesso
DeviceID se SetupAPI non fornisce un modello utile.

Il servizio mantiene il limite di **2048 lookup SetupAPI** per
scansione e di 128 identificativi per proprietà: non vengono
create interrogazioni di rete, download, esecuzioni, modifiche
WUA, driver candidati synthetic o installazioni.

## Esempio di correlazione

WMI: `SOUNDWIRE\SDCA_06&MAN_025D`
SetupAPI della stessa istanza:
`SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711&TYPE_04&VER_01&ADR_01&LID_00&UID_00&SUBSYS_305610EC`

OmegaDrive conserva entrambi, preferendo quello specifico, e
può correlare soltanto l'**Hardware ID esatto** restituito da
una sorgente Windows Update completata. Il token WMI generico
resta non correlabile. La presenza della corrispondenza non
dimostra un aggiornamento più recente, firma CAT, compatibilità,
ranking o installabilità: restano i gate originali.

## Verifica e limiti

Test Core sintetici su AGGREGATOR, AGGREGATEDSPEAKER, SDCA,
con e senza MAN, varianti di maiuscolo, SignedDriver fallback,
prevalenza SetupAPI su SignedDriver, mismatch vendor/SDCA/bus,
ID malformati e conflitti WMI. Verifiche WUA/INF/diagnostica
anonima assicurano l'assenza di false offerte.

Sono ancora necessari un benchmark e una scansione sul vero
PCSpecialist Recoil 16 (#91/#136), soprattutto per NVIDIA
RTX 4060, Intel/Killer e audio. Questo recupero migliora
la **possibilità** di correlare alcuni codec SoundWire,
non prova che nuovi driver siano disponibili.
