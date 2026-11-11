# OmegaDrive — i totali nel report anonimo usano solo GUID WUA verificati (#351)

## Problema

In `Copia copertura`, la prima riga «Windows Update: X candidati
distinti» usava direttamente `DriverSearchReport.Updates.Count`.
Il numero di «associazioni dispositivo incomplete» era anch'esso
calcolato sulle righe raw. L'audit WUA→PnP stampato immediatamente
sotto già adottava regole più rigorose, quindi la stessa copia
poteva riportare **5 candidati** in testa ma **1 GUID** nell'audit:

- duplicati dello stesso GUID fra Windows configurato, Windows Update
  online e Microsoft Update;
- record aggiunti da fonti manuali/OEM/sconosciute;
- record con GUID non valido;
- flag `AdditionalHardwareIdsIncomplete` da fonti estranee.

Questo rendeva meno affidabile il confronto degli otto candidati
segnalati da Driver Easy con la copertura osservata da OmegaDrive
(#91 e #136).

## Risoluzione

`ScanCoverageText.Create` crea una sola volta
`WuaPnpAssociationAudit.Create` e usa direttamente:
- `SearchVerified` per distinguere un pass WUA completato da
  una ricerca non verificata;
- `ConfirmedGuids` come conteggio dei GUID Type=Driver verificati
  e deduplicati fra fonti;
- `IncompleteWuaIds` come numero di GUID verificati con
  identificativi aggiuntivi incompleti;
- `OverBudget` per dichiarare «conteggio non valutabile»
  quando un input legacy/IPC supera i limiti dell'audit.

La copia non interpreta un input fallito, malformato o troppo
grande come zero driver disponibili. I titoli WUA Type=Software
restano indizi separati, il report diagnostico non installa
driver e non autentica pacchetti OEM.

L'audit già rifiuta GUID malformati e origini non riconosciute,
fonde stesso GUID con più Hardware ID dichiarati da WUA e rispetta
i limiti su numero di dispositivi, update e identificativi.
Il messaggio nel report non esporta GUID, Hardware ID, seriali,
nomi dei device, nomi PC o dettagli proxy/WSUS.

## Regressioni automatizzate

Test dedicati con due pass WUA sullo stesso GUID, collisione da
fonte estranea in entrambi gli ordini, provider OEM sconosciuto
isolato, GUID non validi, pass tutti falliti, advanced review,
inventario non verificato, input oltre budget, Software separato
e protezione dei dati privati.

La release CI Windows verifica la logica sintetica. Non prova
che sul PCSpecialist Recoil 16 compaiano driver nuovi rispetto a
Driver Easy: per il gap **8-vs-2** servono ancora report reali
e controllo delle fonti OEM ufficiali (#91, #136, #85).
