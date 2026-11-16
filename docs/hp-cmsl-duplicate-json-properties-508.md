# HP CMSL: chiavi JSON duplicate nei metadati driver (#508)

## Problema
`HpSoftpaqCatalogParser` usava `JsonElement.TryGetProperty`, che non
segnala duplicazioni delle proprietà di un record. Due `Category`
contraddittorie (Firmware e Driver - Graphics) o due `Id` potrebbero
mascherarsi come una singola voce driver HP oppure alterare la deduplica
dei SoftPaq e la fairness fra categorie.

## Correzione su main
- Prima di leggere i campi del record, enumerare **tutte** le proprietà.
- Trattare come ambiguo e scartare il solo record contenente duplicati,
  senza differenziare il case, di `Category`, `Id`, `Name`, `Version`,
  `ReleaseType` o `ReleaseDate`.
- I campi sconosciuti ripetuti non alterano la selezione perché nessun
  valore sconosciuto è letto o riportato.
- I record indipendenti validi restano disponibili; il limite di 1000
  record ricevuti continua a valere **prima** di ogni filtro.
- Nessun campo del record ambiguo viene riportato in output, compresi
  i dati sensibili dell'utente o eventuali endpoint locali.

## Regressioni
`HpCmslDuplicatePropertyBoundaryTests` verifica Category
Firmware/Driver in entrambi gli ordini, Id duplicati e maiuscole
varianti, metadati display ambigui, fairness NVIDIA/Killer/Realtek e
quote di risposta.

Resta un'**anteprima di metadati ufficiali HP**, non una verifica
dell'applicabilità hardware, della firma CAT o della disponibilità
di un aggiornamento installabile. La QA HP reale (#106) resta aperta.
