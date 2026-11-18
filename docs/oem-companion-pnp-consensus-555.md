# Conteggi OEM companion allineati al consenso PnP (#555)

I provider OEM NVIDIA, Intel e AMD pubblicano uno stato *read-only* con
`DetectedDeviceCount`. Questo numero misura presenze locali riconosciute,
non driver mancanti, versioni pubblicate o pacchetti installabili.

## Problema

Il conteggio OEM prima raggruppava `DeviceId` senza normalizzare gli spazi
ASCII ai bordi (mentre la Home e l'audit WUA/PnP lo fanno). Due righe
WMI/SignedDriver con lo stesso ID istanza ma padding venivano contate come
due dispositivi. Le righe con due Hardware ID PCI incompatibili **dello
stesso vendor** superavano inoltre il controllo che verificava solo il
consenso del produttore: il conteggio OEM poteva attestare un device che
`PnpDistinctTrustedDevices.Create` aveva scartato.

## Correzione

- Grouping sullo stesso `NormalizeInstanceKey` usato dal resto della UI.
  Si normalizza **solo la chiave**, non l'ID PnP originale.
- Ogni gruppo deve superare `PnpDistinctTrustedDevices.Create` prima di
  concorrere al conteggio OEM: modello primario, vendor e SUBSYS devono
  essere coerenti. Nessuna correlazione da device separati.
- Resta la precedenza del vendor PnP sul nome/provider installato e il
  fallback legacy solo in assenza di un modello pubblico riconosciuto.
- Un modello PCI base confermato da un'unica variante SUBSYS resta valido;
  due SUBSYS diversi o due DEV diversi falliscono chiuso.
- Test Shared.Core + test dei provider reali (NVIDIA/Intel) esercitano il
  `DetectedDeviceCount` e l'indicatore `IsApplicable`.

## Limiti

Nessuna nuova fonte NVIDIA/Intel/AMD interrogata, nessun download, nessun
driver installato. QA effettiva Recoil 16 / 8-vs-2 e Windows E2E rimangono
#91, #136 e #85.
