# Matrice OEM: budget per istanze PnP uniche, non righe WMI (#543)

## Problema
`VendorInventoryCoverage` tagliava prima a 2.048 righe l'elenco WMI
grezzo e soltanto dopo deduplicava per `DeviceId`. Migliaia di
rappresentazioni della stessa scheda Intel potevano consumare l'intero
budget, nascondendo una GPU NVIDIA o un codec Realtek ancora presenti
nell'inventario completo. Inoltre un duplicato contraddittorio della
stessa istanza dopo la soglia non poteva invalidare il primo modello.

## Correzione
- Il tetto di **2.048** si applica alle istanze `DeviceId` distinte.
- Il massimo di **8.192 righe grezze** limita CPU e memoria anche con
  snapshot IPC anomali. Le ulteriori rappresentazioni di un'istanza
  già ammessa vengono valutate entro questo secondo budget.
- `PnpDistinctTrustedDevices` continua a escludere una stessa istanza
  con modelli o vendor primari incompatibili: non si uniscono Hardware
  ID provenienti da istanze diverse.
- Se almeno uno dei due budget è superato, `InventoryComplete=false`.
  L'output parla di inventario **parziale** e non di assenza verificata
  di ulteriori periferiche/driver.
- Test: duplicati Intel prima di NVIDIA/Realtek, conflitto tardivo
  NVIDIA/Realtek, esattamente 2.048 istanze uniche, eccedenza di
  istanze distinte, eccedenza di righe grezze e report anonimo WUA.

## Limiti
È una correzione del conteggio locale dei dispositivi. Non recupera
nuovi pacchetti, non certifica compatibilità OEM e non modifica
`Aggiorna tutto`. Per il Recoil 16 restano necessari risultati
Type=Driver/Type=Software/OEM reali (#91/#136) e verifiche CAT/INF
e ranking delle sorgenti OEM (#128/#130).
