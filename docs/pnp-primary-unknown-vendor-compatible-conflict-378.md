# PnP: un modello primario di vendor sconosciuto non può ereditare un altro vendor dal Compatible ID (#378)

## Difetto in v0.0.237

`PnpKnownVendorIdentity.FromDriver` riconosceva le identità PnP ufficiali
soltanto per un elenco di produttori supportati. Se un Hardware ID
`USB\\VID_9999&PID_0001` o `HDAUDIO\\FUNC_01&VEN_9999&DEV_0001`
era ben formato ma il produttore sconosciuto, il codice lo trattava come
assenza di identità. Un Compatible ID di un *altro* vendor noto poteva così
attribuirgli NVIDIA, Intel, Realtek, Qualcomm, Logitech o Razer.

La Home, i riepiloghi OEM e i collegamenti supporto avrebbero mostrato
il vendor errato nonostante la presenza dell'Hardware ID primario. Non
veniva identificato alcun nuovo driver installabile; era una falsa
attribuzione di copertura per il catalogo.

## Correzione conservativa

- Gli Hardware ID con produttore **già riconosciuto** continuano ad avere
  precedenza sui Compatible ID, mantenendo il comportamento esistente.
- Se non è stato riconosciuto alcun vendor dagli Hardware ID ma esiste
  un **modello validato con produttore esplicito**, e i Compatible ID
  suggeriscono un vendor noto, la contraddizione viene segnalata con
  `PnpKnownVendorIdentity.Vendor.Conflicting`: nessun falso handoff OEM.
- La verifica usa il parser pubblico stretto già esistente; stringhe
  con percorsi/seriali, suffissi non validi e ID PnP generici
  non diventano nuove identità autorevoli.
- Hardware ID generici quali `ROOT\\NONSTANDARD`, `PCI\\CC_030000`
  e `SOUNDWIRE\\AGGREGATOR` mantengono il fallback da Compatible ID
  **specifici**, perché non contengono un vendor primario.
- I dati con vendor sconosciuto *senza* alcun Compatible ID riconosciuto
  rimangono sconosciuti e non vengono trasformati in update.

I test sintetici coprono vendor primari sconosciuti USB, PCI, HDAUDIO,
SWC, HID, SNDW, SoundWire; secondari NVIDIA, Intel, Realtek, Qualcomm,
Logitech/Razer; Home, link OEM, contatori companion, scelta Driver Store
read-only e report anonimo. È un miglioramento di classificazione,
NON una nuova ricerca WUA/Microsoft Update/OEM, installazione o firma
CAT. Il confronto hardware Recoil 16 8-vs-2 (#91/#136), la copertura
effettiva #100 e l'E2E Windows #85 restano aperti.
