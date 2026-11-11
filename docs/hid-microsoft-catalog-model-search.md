# OmegaDrive — ricerca manuale Microsoft Update Catalog per HID (#347)

## Gap risolto (v0.0.206)

La ricerca manuale Microsoft Update Catalog accettava modelli pubblici PCI,
USB, HDAUDIO, INTELAUDIO, SWC, NVIDIA ACPI e SoundWire. Un driver per
un modello HID con Vendor ID e Product ID specifici (mouse Logitech
`HID\VID_046D&PID_C534`, periferiche Razer
`HID\VID_1532&PID_0100`) non produceva invece il collegamento,
benché i modelli fossero già validati per la correlazione PnP.

## Comportamento

`MicrosoftUpdateCatalogSearch.TryGetPublicModelKey` ora accetta
solo `HID\VID_XXXX&PID_YYYY` più un insieme limitato di suffix
Windows: `&REV_XXXX`, `&MI_XX`, `&IG_XX`,
`&COLXX` (token esadecimali). La sintassi deve soddisfare
anche `HidPnpMatchPolicy.IsSpecificHardwareId` e le regole
generali sui caratteri/singola separazione bus. La query sul Catalog
è sempre soltanto il **modello pubblico** `HID\VID_XXXX&PID_YYYY`
in uppercase.

Il flusso è **soltanto manuale**, su gesto esplicito dell'utente,
URL HTTPS con host fisso `www.catalog.update.microsoft.com`.
Il servizio non interroga il Catalog automaticamente, non
trasmette ID PnP di istanza, seriali, nome dispositivo,
suffissi dell'interfaccia/collezione, OEM SUBSYS o percorsi locali.
Anche un risultato sul Catalog NON dimostra che il pacchetto
sia recente, idoneo al sistema, firmato CAT o installabile.

I token generici `HID_DEVICE`, VID-only e
`HID\VID_046D&UP:0001_U:0006` (usage senza PID)
NON generano query, e non vengono trasformati in match WUA
per dispositivo. La correlazione HID VID/PID con suffissi
diversi resta eventualmente una **review-only** già esistente,
non una scorciatoia per `Update All`.

## Verifica

La suite Core include test per Logitech/Razer,
suffissi reali/ordine, fonti CompatibleID più deboli,
regex incomplete, token di classe, usage senza PID,
istanze e injection, privacy URL e separazione
tra collegamenti manuali e veri aggiornamenti Windows.

Rimane aperta l'integrazione di una fonte ufficiale Logitech
automatizzabile (#27); così come il confronto reale dei
candidati Driver Easy sul Recoil 16 (#91, #136). Non è
corretto attribuire nuove offerte WUA a questa modifica.
