# Audit WUA→PnP: quote coerenti con la matrice OEM (#544)

## Problema

La matrice `VendorInventoryCoverage`, dopo #543, gestisce fino a
2.048 istanze `DeviceId` distinte in 8.192 righe WMI/SignedDriver.
L'audit aggregato `WuaPnpAssociationAudit` continuava invece a
rifiutare come oltre budget l'intero inventario non appena erano
presenti 2.049 **righe raw**. Migliaia di copie della stessa scheda
Intel potevano far apparire `associazioni non valutabili` nel report
copiabile anche se il conteggio Home rilevava correttamente le schede
NVIDIA/Realtek successive e i GUID WUA Type=Driver già confermati.

## Correzione

- La diagnostica anonima ora distingue il numero di righe WMI grezze
  dal numero di istanze PnP uniche: max 8.192 righe e 2.048 `DeviceId`.
- Mantiene tutte le rappresentazioni **delle istanze ammesse** entro
  la quota raw. `PnpDistinctTrustedDevices` può quindi escludere
  NVIDIA/Realtek/Intel contraddittori anche se la seconda riga arriva
  dopo il vecchio limite 2048.
- Se si eccede anche **una sola** quota (righe raw, ID distinti,
  ID PnP o ID WUA), l'audit restituisce `OverBudget` e non presenta
  le associazioni come valutate o l'assenza come accertata.
- Le quote sono derivate dalle costanti già usate dalla matrice,
  evitando divergenze future. I conteggi rimangono per GUID WUA
  Type=Driver da pass validi, con corrispondenza Hardware ID esatta,
  Compatible ID o debole solo come indizio da rivedere.
- Test sintetici: Intel ripetuto 2.300 volte con NVIDIA e Realtek
  successivi, identità contraddittoria tardiva, 2.048 vs 2.049
  istanze, 8.192 vs 8.193 righe, report anonimo senza seriali/ID.

## Ambito e verifiche mancanti

La modifica migliora soltanto la **diagnostica di hardware e offerte
WUA già acquisite**. Non aggiunge una fonte driver OEM, non attesta
che un driver sia più recente o installabile e non autorizza download.
Il confronto reale degli otto candidati Recoil 16 (#91/#136) e le
firme/ranking CAT/INF OEM (#128/#130) rimangono aperti.
