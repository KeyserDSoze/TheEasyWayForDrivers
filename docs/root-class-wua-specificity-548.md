# ID ROOT di sola classe: non sono un modello WUA specifico (#548)

## Gap
Il validatore `PnpIdentifierFallback.IsStrictSingleId` richiede un token
PnP sintatticamente corretto, ma non garantisce che sia il modello di
un dispositivo. Prima del fix il matcher accettava `ROOT\MEDIA`,
`ROOT\SOFTWARE`, `ROOT\NET`, ecc. come corrispondenze esatte:
più istanze Windows possono condividere lo stesso identificativo di
classe. Una corrispondenza per testo non prova un aggiornamento specifico.

## Modifica
`DriverUpdateMatcher.IsCorrelatableHardwareId` esclude soltanto dieci
alias ROOT di classi generiche, case-insensitive, dopo la validazione
del token (continuano a essere ammessi soltanto spazi ASCII ai bordi).
I nomi **specifici** come `ROOT\NVVAD`, `ROOT\VIRTUAL_ADAPTER` e
`ACPI\NVDA0820` conservano i match esatti. Gli ID PnP locali originali
non sono riscritti.

La modifica è condivisa da indice di matching per-device, diagnostica
WUA→PnP, correlazione OEM/INF e altre verifiche read-only che riusano
il matcher. Il GUID WUA rimane nel conteggio delle offerte effettivamente
restituite, ma non diventa una prova di dispositivo specifico.

Non è stato modificato `QuickUpdatePolicy`, né l'installazione globale
di un aggiornamento realmente offerto da Windows Update, né sono state
create installazioni OEM automatiche.

## Test e limiti
La suite include alias ROOT generici, modello virtuale valido,
Compatible ID audio specifico, aggregazione GUID e report copiabile
privo di identificativi privati. Questo non sostituisce la QA reale su
Windows 11 né il confronto Recoil 16 (#91/#136/#85).
