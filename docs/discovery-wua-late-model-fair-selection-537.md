# Discovery+ WUA: scelta equa degli Hardware ID oltre 1024 (#537)

## Problema
Il confine privacy #536 validava i token WUA in Discovery+ soltanto
nelle prime 1024 entry del campo `AdditionalHardwareIds`. Vecchi
snapshot IPC o rappresentazioni legacy con 1600 modelli di un solo
produttore prima del driver NVIDIA RTX 4060, Intel Killer o Realtek
facevano scomparire questi ultimi **dal matching per-device Discovery+**,
nonostante fossero già presenti nella medesima offerta WUA.

## Correzione
- Elaborare al massimo 2560 entry legacy, come
  `WuaDriverEntryLimits.MaximumEntriesToInspect`.
- Prima escludere ID PnP malformati/privati e duplicati (incluso il
  primary); poi usare `WuaDriverEntryIdSelection.Select`, la policy
  vendor-fair già testata dal lettore WUA COM.
- L'output rimane limitato a 1024 ID distinti. Se le entry originali
  eccedono 1024, sono malformate, o erano già contrassegnate come
  incomplete, resta `WuaIdsIncomplete=true` per non dichiarare una
  falsa assenza di driver.
- Test con GPU NVIDIA, Wi-Fi Killer, Realtek audio tardivi, input
  malformati, dedup, quota massima 2560 e compatibilità per liste
  già complete entro 1024.

Il fix conserva i GUID, i controlli di privacy e la natura **sola
lettura** di Discovery+. Non cambia il servizio WUA o l'installabilità,
non interroga alcun nuovo catalogo OEM e non verifica i driver su
hardware Recoil 16 (#91/#136).
