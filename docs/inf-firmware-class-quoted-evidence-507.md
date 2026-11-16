# Quote-safe setup-class evidence per firmware OEM (#507)

## Problema
`InfPlatformPreflight.IsFirmwareUpdateClass` usava `Trim('"')` sui
campi `Class` e `ClassGuid`. Una dichiarazione come
`Class="""Firmware"""` contiene quote *letterali escaped* e non e
equivalente a `Class=Firmware`, ma poteva essere classificata come
firmware. Questo nascondeva confronti OEM ordinari e cambiava la priorita
nei 12 controlli CAT/INF.

## Implementazione
- `GetFirmwareClassEvidence` valuta tutti i campi `Class`/`ClassGuid`
  come un unico scalar INF con `ParseVersionScalar` condiviso.
- Stati: `Firmware` per dichiarazioni valide esplicite,
  `Ambiguous` per metadati duplicati/malformati e
  `NoFirmwareEvidence` per dichiarazioni ordinarie non ambigue.
- Una dichiarazione firmware positiva e valida prevale anche su una
  dichiarazione non-firmware contraddittoria: non si promuove mai un
  firmware esplicito ad update ordinario.
- Nel confronto ZIP/CAB/cartella un INF `Ambiguous` conserva il PnP
  matching per revisione, ma versione e data sono non confrontabili,
  il verdetto e manuale e `IsPotentialNewer` e falso.
- Il planner CAT/INF della cartella non usa slot per tali metadata
  non interpretabili: conta l'INF fra gli scartati, senza dichiararne
  firma o applicabilita.
- Sintassi quoted `Class="Firmware"` e
  `ClassGuid="{f2e7dd72-6468-4e36-b6f1-6488f42c1b52}"` valida.

## Regressioni
`InfFirmwareClassQuotedEvidenceTests` copre quote escaped,
suffix garbage, doppi campi, ClassGuid, NVIDIA/Killer, ZIP e audit
cartella con CAT sintetiche non firmate.

La classificazione resta una analisi locale *read-only*, non sostituisce
SetupAPI, firme CAT autentiche o QA Windows reale (#128, #130, #136).
