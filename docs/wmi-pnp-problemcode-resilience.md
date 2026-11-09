# OmegaDrive — isolamento del codice problema WMI PnP (#321)

## Problema

`WmiDriverInventory.Scan` enumerava `Win32_PnPEntity` e passava
`ConfigManagerErrorCode` direttamente a `Convert.ToUInt32`.
Un singolo valore malformato o overflow interrompeva l'intera scansione,
occultando i device NVIDIA/Intel/Killer/Realtek non coinvolti.

## Implementazione

- `WmiPnpProblemCodeReader` accetta solo numeri interi non negativi
  `uint`-compatibili o stringhe ASCII decimali al massimo di dieci cifre.
  Varianti malformate, `null`, overflow e getter WMI/COM non accessibili
  diventano `Verified=false`, senza loggare l'eccezione o l'ID dispositivo.
  Cancellazione e errori di programmazione rimangono propagati.
- Ogni riga `Win32_PnPEntity` mantiene Hardware/Compatible IDs, data
  firmata e identità SetupAPI indipendenti dal codice PnP opzionale.
- `DriverInfo.PnpProblemCodeVerified` è un nuovo booleano IPC opzionale,
  default `true` per vecchi payload. Un codice sconosciuto non prova
  uno stato Windows sano (0), un errore PnP o un driver mancante (28).
- Home, elenco/particolari del dispositivo e report copiabile mostrano
  `Da verificare` / `Non verificato`, contatori anonimi senza seriali,
  e consiglio di controllare Gestione dispositivi.
- Nessuna nuova offerta WUA, firma CAT, update OEM o installazione. Non
  trattare la resilienza della scansione come prova che gli 8 candidati
  Driver Easy siano driver installabili sul Recoil 16.

## Validazione

Test sintetici per uint/varianti numeriche, null, stringhe non numeriche,
overflow, getter COM falliti, cancellazione, status e JSON IPC legacy.
QA fisica Recoil 16 e confronto 8-vs-2 restano #136/#91; firma
Authenticode resta #283.
