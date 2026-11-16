# Firmware/BIOS INF nei pacchetti OEM locali (#497)

## Diagnosi
Windows può usare INF con `Class=Firmware` e il GUID setup class
`{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}` per distribuire un
aggiornamento firmware. La comparazione read-only di ZIP/CAB/cartella
OEM valutava solo PnP ID, TargetOSVersion e DriverVer: anche un INF
Firmware con numero/data più recenti poteva comparire nel conteggio
«potenziali driver più nuovi» del risultato.

Fonte Microsoft: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/updating-device-firmware-using-windows-update

## Modifica
- `InfPlatformPreflight.IsFirmwareUpdateClass` analizza **tutti** i
  campi `Class` e `ClassGuid` in `[Version]`, senza caricare o
  eseguire l'INF. Rileva Firmware, BIOS e GUID setup class Firmware,
  anche quando `Class` e `ClassGuid` si contraddicono.
- `LocalOemDriverComparison` conserva il matching Hardware/Compatible
  ID ma marca `Finding.IsFirmwareInf` e azzera ogni confronto
  DriverVer/versione data per i firmware. Nessun firmware INF entra in
  `PotentialNewerDevices` o in una installazione.
- Per lo stesso Hardware ID, un normale INF driver prevale sul firmware
  nell'anteprima diagnostica, a parità di forza ID.
- Il codice condiviso copre ZIP e comparazione cartella OEM; la
  pipeline CAB richiama lo stesso confronto quando recupera INF.

## Limiti
La classificazione non verifica firma CAT, membership, applicabilità
Windows SetupAPI, versioni firmware effettive o ranking. Il nome
`Firmware` viene dalla classe dichiarata dall'INF e non è
un'attestazione di autenticità. QA Windows reale #85/#136 e
verifica firme/dipendenze #128/#130 restano da completare.
