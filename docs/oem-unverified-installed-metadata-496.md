# OEM INF/PnP: metadati del driver Windows attendibili (#496)

## Problema
La comparazione ZIP/CAB/cartella di INF OEM correlava correttamente Hardware
ID e Compatible ID PnP, ma il controllo numerico e di data `DriverVer`
usava anche valori `DriverVersion` e `DriverDate` provenienti da un
record Windows con `DriverMetadataQuerySucceeded=false` oppure
`HasDriver=false`. Questi valori possono essere legacy/incompleti e
generare falsi `IsPotentialNewer` e conteggi aggiornamenti più recenti.

## Modifica
- La corrispondenza PnP/INF rimane indipendente dal lookup WMI SignedDriver.
  NVIDIA, Killer e Realtek continuano a comparire come evidenza read-only
  quando gli Hardware ID sono verificabili.
- Si attivano **confronto versione, confronto data e suggerimento versione
  superiore** soltanto per dispositivi con `DriverMetadataQuerySucceeded &&
  HasDriver`. In caso contrario la versione Windows è esplicitamente
  `non verificata`, la relazione numerica è `Unavailable` e la data
  `NotVerified`, senza promozione `IsPotentialNewer`.
- Si mantiene la priorità `TargetOSVersion` del preflight e la separazione
  fra `HardwareId`, `CompatibleId`, review-only e ranking SetupAPI.
- Test anche sul codice cartella OEM, che riutilizza `Compare` senza
  cambiamenti o operazioni sui driver.

## Limiti
Non aggiorna Windows, non introduce nuovi pacchetti OEM, non estrae o
esegue file binari, non classifica un ranking per ZIP/CAB esterni.
La certezza delle offerte reali su PCSpecialist Recoil 16 richiede QA
fisico e confronto col portale ufficiale (#120/#136/#91); firma e
dipendenze OEM #128/#130 ancora aperte.
