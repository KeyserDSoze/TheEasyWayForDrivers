# Handoff OEM: indicizzazione unica degli Hardware ID WUA (#493)

## Diagnosi
La preview `VendorSourceDiscovery.FindWithCoverage` enumerava ogni GUID WUA
con fino a 1024 associazioni per **ogni** dispositivo riconosciuto.
Il costo cresceva con dispositivi PnP × GUID WUA × entry-ID del GUID,
rallentando le scansioni estese benché fosse già disponibile un indice
PnP→GUID usato dalla schermata dei dispositivi.

## Implementazione
- Preparare una volta `DeviceWuaMatchGroups.Prepared` (merge canonico D/B/N/P,
  filtraggio origini WUA e indice read-only con cap 250.000 associazioni).
- Usare `Prepared.ForDevice` per tutti gli handoff OEM e contare esclusivamente
  `Ordinary + ReviewOnly`: GUID con ID hardware o compatible esatti.
- Escludere `ModelOnlyReview` dal conteggio «candidati WUA per ID»; un
  modello senza SUBSYS uguale non equivale ad associazione esatta.
- Mantenere il fallback conservativo dell'indice in caso di over-budget,
  le 100/150 righe, la rotazione fra vendor, la somma dispositivi eleggibili,
  gli URL HTTPS allowlist e `IsInstallable = false`.

## Verifiche
Fixture NVIDIA GPU/HD Audio, Killer Wi-Fi, Realtek audio con Compatible ID,
GUID duplicati D/B da WU/MU, indizi weak-only esclusi, origini sconosciute,
ID malformati e 500 dispositivi / 104 GUID / 12.800 Hardware ID sintetici.
Queste fixture non interrogano nuovi cataloghi e non risolvono il confronto
Driver Easy 8 vs ~2 (#91/#136), né sostituiscono QA Windows reale (#85).
