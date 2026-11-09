# OmegaDrive — lettura campi scalari WMI per dispositivo (#327)

## Problema
`WmiDriverInventory.GetString` e `GetBoolean` accedevano direttamente
a proprietà COM WMI opzionali. Un solo getter `DriverVersion`,
`DriverProviderName`, `DriverDate`, `IsSigned` o simile malformato
faceva fallire l'intera query `Win32_PnPSignedDriver`: la policy
correttamente conservativa eliminava **tutti** i metadati, compresi
NVIDIA/Intel/Killer/Realtek sani. Un guasto a `Name`/`PNPClass`/
`Manufacturer` in `Win32_PnPEntity` poteva oscurare altri
dispositivi.

## Regola implementata
`WmiOptionalFieldReader` legge ogni singola proprietà in sola lettura,
isolando `ManagementException`, `COMException`,
`UnauthorizedAccessException`, `SecurityException` e
`InvalidCastException` **solo sul getter opzionale**.
Le stringhe devono essere effettivamente `string`, non `ToString()`
su una variante COM arbitraria; limite 1024 caratteri, controlli e NUL
rifiutati. Boolean `IsSigned` solo se la variante è veramente `true`.
La mancata lettura di un campo non dice «driver assente» e non
certifica firma CAT/Authenticode.

Una riga firmata con versione/INF valido resta disponibile anche se
un altro getter fallisce; per dispositivo PnP il nome guasto usa il
fallback descrittivo privo di seriale. Il DeviceID non leggibile
rende inutilizzabile **solo quella riga**, senza matching per nome.
Le eccezioni nell'**enumerazione WMI** rimangono soggette alla policy
fail-closed che scarta snapshot incompleti. Cancellazione ed errori
di programmazione non sono nascosti.

Test sintetici dei getter indipendenti, cast e COM, input ostili,
privati e troppo lunghi, metadati successivi, cancellazione e query
incomplete. Non sostituiscono la QA Recoil 16 (#136), il confronto
Driver Easy (#91) o la firma (#283). Nessuna modifica a
`Aggiorna tutto`, installazioni, WUA e download OEM.
