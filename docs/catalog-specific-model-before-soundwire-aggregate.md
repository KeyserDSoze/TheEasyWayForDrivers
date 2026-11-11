# OmegaDrive — preferenza del codec specifico sul generico SoundWire nel Catalog manuale (#358)

## Difetto in v0.0.217

La ricerca browser-only \`MicrosoftUpdateCatalogSearch.Create\` sceglieva
il **primo Hardware ID** con formato valido. Windows può enumerare
\`SOUNDWIRE\AGGREGATOR\` o \`SOUNDWIRE\AGGREGATEDSPEAKER\` prima di un
codec come \`SOUNDWIRE\SDCA_06&MAN_025D&FUNC_0711\`, o un identificativo
SNDW/ACP completo che OmegaDrive sa già leggere (#353/#354/#357).

I token aggregati sono validi come **ricerche di categoria audio
generica**, ma non identificano uno specifico codec. Perciò l'utente
poteva aprire una ricerca troppo generica anche quando lo stesso
inventario PnP conteneva il modello pubblico specifico.

## Nuova selezione

\`PreferredModelQuery\` considera gli ID in ordine:
- tra gli **HardwareIds**, seleziona il primo modello specifico,
  tenendo il primo aggregatore SoundWire riconosciuto come fallback;
- se nessun HardwareID genera una ricerca ammissibile, applica lo
  stesso criterio ai **CompatibleIds**, etichettandoli esplicitamente
  come evidenza più debole.

Un Hardware ID aggregato rimane preferito a un modello più
specifico presente **soltanto nei CompatibleIds**: questo preserva
la precedenza della fonte di identità ed evita di promuovere
un codice compatibile a prova hardware. Solo in assenza di un
modello specifico nella *stessa lista* si usa la ricerca di
categoria aggregata. L'ordine relativo fra modelli specifici
resta stabile.

Tutti gli ID sono validati dalle stesse grammatiche del
Catalog pubblico già presenti. Il link usa soltanto modelli
pubblici: i token SDCA o SNDW vengono normalizzati
senza indirizzo, UID, SUBSYS, seriale o DeviceId e l'host
HTTPS Microsoft rimane fisso. Token malformati, identificativi
di istanza e suffix arbitrari non diventano query.

## Sicurezza e limiti

Il cambiamento riguarda **solo la selezione del link per
ricerca manuale**, non l'inventario WMI, il matcher WUA/INF,
il numero di GUID, Update All, i download o i controlli CAT
e Windows install-time. \`IsInstallable\` e
\`CatalogWasQueried\` restano **false**.

I test Core verificano entrambi gli aggregatori, codec
SoundWire SDCA/SNDW/ACP e GPU NVIDIA, differenza tra
Hardware ID e Compatible ID, input malformati e privati,
large-list, URL e l'assenza di driver sintetici.

La correzione può facilitare la ricerca manuale di un
codec, ma non dimostra l'esistenza di aggiornamenti
aggiuntivi sul PCSpecialist Recoil 16, né risolve
il benchmark Driver Easy **8 contro 2** (#91/#136).
