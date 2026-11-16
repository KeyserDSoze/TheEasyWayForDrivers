# WUA Partial: titolo COM facoltativo senza perdita di altri indizi (#514)

## Bug
Su una risposta WUA `SucceededWithErrors` o con warning, la scansione
non promuove le entry Type=Driver ad aggiornamenti: può soltanto
raccogliere titoli anonimi come **indizi non verificati e non installabili**.
`WuaPartialResultReader.ReadAll` leggeva il getter COM `Title`
senza isolamento: un unico titolo inaccessibile interrompeva la
lettura ed eliminava anche gli altri indizi già validati o successivi,
nonostante l'enumerazione dell'insieme potesse ancora completarsi.

## Correzione
- Solo il getter facoltativo `Title` è isolato in `ReadOptionalTitle`.
  Titolo COM illeggibile, `null` o di tipo inatteso => scarta la
  **sola riga informativa**.
- Nessun `ToString()` su VARIANT COM; stringhe di errore e metadati
  potenzialmente privati non sono aggiunti al report.
- `Item`, `Identity`, `UpdateID`, conteggio e cancellazione restano
  strutturali: errori di lettura impediscono la restituzione dell'intero
  risultato, non pubblicano una porzione ingannevole.
- `OperationCanceledException` e `OutOfMemoryException` propagano.
- Tutti i titoli sopravvissuti passano comunque per
  `WuaPartialDriverHintCollector.TryCreate` (GUID valido, origine
  allowlist, titolo sicuro), massimo 4000 risultati letti per pass.
- Un pass `Partial` rimane `Partial`: nessun nuovo
  `DriverUpdateInfo`, approvazione di installazione, modifica dei
  servizi WUA o collegamento PnP affidabile.

## Regressioni
`WuaPartialTitleGetterIsolationTests`: titolo COM guasto in mezzo a
NVIDIA/Killer/Realtek, VARIANT non-string e titoli malformati,
`Item/Identity/UpdateID` strutturalmente guasti, cancellazione,
out-of-memory, privacy, quota 4000, nessuna promozione all'installazione.

Resta necessario il collaudo offline/WSUS su un vero PC Windows (#92)
e il confronto driver Recoil 16 (#91/#136): la CI sintetica non
dimostra l'esistenza di un nuovo aggiornamento hardware.
