# WUA: slot protetti per SNDW/ACP oltre 64 vendor rari (#362)

## Caso riproducibile

Dopo i primi 960 Hardware ID di un GUID WUA, `WuaDriverEntryIdSelection`
usava il medesimo gruppo `other` per tutti i modelli di vendor PCI/USB/HID
rari **e** per codec moderni SNDW e ACP. Se 65 o più famiglie distinte di
produttori rari precedevano un codec SoundWire/AMD ACP valido, i 64 slot
residui potevano esaurirsi: l'ID esatto già dichiarato da WUA veniva scartato
dal trasporto, pur essendo rilevabile localmente dal PnP.

## Soluzione circoscritta

Rimangono **64** slot riservati e **1.024** ID IPC per GUID. I primi **960**
sono invariati. Nel round-robin, i gruppi 0–7 (major vendor + SoundWire SDCA)
rimangono invariati; SNDW (gruppo 8) e ACP (gruppo 9) hanno una possibilità
indipendente, e le altre famiglie specifiche restano nel gruppo 10. I modelli
SNDW/ACP devono passare la stessa grammatica rigorosa
`ModernAudioPnpMatchPolicy.IsSpecificHardwareId` usata dal match WUA/INF.
Le famiglie rare continuano a ricevere priorità sulla ripetizione di una
famiglia già rappresentata, e le posizioni non usate vengono riempite con
l'ordine originale WUA.

Nessuna modifica a COM, download, installazione, firmware, UI o OEM.
L'ID originale, anche con `SUBSYS`, rimane indispensabile per il match esatto;
la selezione non attribuisce nuovi GUID e non certifica applicabilità.

## Test e limiti

Fixture di 1.250 ID comuni + 110 vendor PCI distinti + SNDW/ACP/SDCA/NVIDIA
tardivi, negativi per CLASS/MAN/DEVTYPE incompleti e stringhe private,
verifica correlazione WUA/INF, dedup, limite 1.024, input non troncato e
conservazione dell'ordine quando l'elenco è breve.

Questo test sintetico non prova nuove offerte WUA né la risoluzione degli
8 driver di Driver Easy vs ~2 visti su Recoil 16. QA e cataloghi reali restano
tracciati in #91, #100 e #136.
