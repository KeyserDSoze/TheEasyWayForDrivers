# OmegaDrive — coerenza delle associazioni WUA nel post-scan (#346)

## Problema

Dopo #345, il confronto delle otto categorie Intel/Killer/NVIDIA
filtrava correttamente i GUID e le fonti Windows Update riconosciute.
Alcune altre superfici diagnostiche (Home vendor, PnP per dispositivo,
audit WUA→PnP) usavano invece soltanto `Guid.TryParse`, o
nemmeno quello, accettando GUID di record **solo OEM/esterni**.

Di conseguenza potevano apparire candidati WUA, review-only e
avvisi «ID WUA incompleti» che non provenivano da un pass WUA.
Un GUID, anche valido, non autentica la provenienza.

## Regola condivisa

`WindowsDriverCandidateMerge.IsKnownWuaGuid` accetta soltanto
un GUID valido con nome di sorgente Type=Driver riconosciuto
dal piano WUA (incluse le vecchie etichette esatte). Il suffisso
` · avanzato` è accettato soltanto se anche
`IsAdvancedCandidate` è vero.

Questa validazione delle etichette non prova da sola che
Windows Update sia stato realmente interrogato: i consumer
con `DriverSearchReport` devono anche richiedere
almeno un pass Type=Driver completato con esito positivo.

Il filtro condiviso è applicato a:
- `WuaDiagnosticAssociations` (schede Home e copertura vendor);
- `DeviceWuaMatchGroups` (correlazioni per dispositivo, incluso
  l'elenco review-only);
- `WuaPnpAssociationAudit` (conteggi degli update GUID);
- `ReferenceDriverCoverage` (otto categorie);
- segnalazione di ID incompleti nel riepilogo Home.

DUE rappresentazioni con il medesimo GUID provenienti da fonti
WUA riconosciute mantengono gli Hardware ID validi, anche quando
appartengono a dispositivi diversi (ad esempio GPU e audio),
entro il limite di trasporto di 1.024 ID aggiuntivi.
Il merge conserva i veti WUA firmware/BrowseOnly e
l'installazione continua a dipendere dalla policy WUA e dal
ricontrollo privilegiato. Gli indizi per modello debole rimangono
**solo revisione**.

Gli aggiornamenti OEM/manuali restano negli appositi canali.
Nessun catalogo viene interrogato né alcun driver installato
da questa correzione. I test sintetici non sostituiscono il
confronto reale Driver Easy 8-vs-2 sul Recoil 16 (#91/#136).
