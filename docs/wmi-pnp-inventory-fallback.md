# OmegaDrive — inventario PnP resiliente ai metadati firmati WMI (#156)

## Difetto reale individuato

Il servizio eseguiva `Win32_PnPSignedDriver` **prima** di `Win32_PnPEntity`. Un errore della query opzionale sui driver firmati bloccava anche la query PnP indipendente: la lista NVIDIA/Intel/Killer/Realtek e il successivo matching WUA rimanevano non verificati.

La classe Microsoft [Win32_PnPEntity](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-pnpentity) rappresenta i dispositivi Plug-and-Play presenti in Gestione dispositivi e fornisce `PNPDeviceID`, `HardwareID`, `CompatibleID`, `PNPClass` e `ConfigManagerErrorCode`. `Win32_PnPSignedDriver` fornisce invece metadati di versioni, INF e firme. L'assenza dei metadati non prova l'assenza del driver.

## Implementazione

- Il lettore di `Win32_PnPSignedDriver` è stato separato: errori recuperabili WMI/COM/accesso negato/security producono **metadata unavailable** e non impediscono `Win32_PnPEntity`. Nessuna query scrive informazioni Windows o installa driver.
- La lettura della query metadati è **all-or-nothing**. Se fallisce dopo alcune righe, non ricicla una tabella parziale. Anche una risposta completata ma con **zero righe** non prova che tutti i dispositivi siano privi di driver: i metadati vengono considerati non verificati.
- La query PnP rimane **obbligatoria**: errore o zero istanze utilizzabili impedisce di dichiarare un inventario completo con zero dispositivi.
- `DriverInfo.DriverMetadataQuerySucceeded` è un parametro opzionale finale, default `true`, per preservare la deserializzazione dei vecchi JSON IPC.
- Se `false`, il dispositivo sano con `HasDriver=false` viene presentato **Da verificare**, non conteggiato in `NeedsAttention`. Un vero errore Windows 28/43 continua a richiedere attenzione anche senza metadati.
- Home e report anonimo mostrano il conteggio di dispositivi con **versioni installate non verificate** e avvertono che non sono necessariamente driver mancanti.
- Ricerca `Type=Driver`, confronto Hardware/Compatible IDs, fonti OEM, `Aggiorna tutto` e politica di installazione invariati.

## Test e privacy

- [x] Query metadata riuscita o vuota, errori `ManagementException`/`COMException`/Access Denied/SecurityException, cancellazione e eccezioni inattese.
- [x] `DriverInfo` con metadata incomplete: nessun falso aggiornamento/missing driver; conservare errori veri Windows 28 e 43.
- [x] NVIDIA GPU riconosciuta attraverso Hardware ID anche quando nome, provider e versione sono generici o assenti.
- [x] Report aggregato senza seriali/Hardware ID/nome dispositivo; serializzazione IPC retrocompatibile.
- [ ] CI Windows .NET 10, xUnit Core+Service, compilazione, IPC e contratto WPF.
- [ ] QA Recoil 16 (#136): confronto inventario PnP con/senza lookup `Win32_PnPSignedDriver`, includendo GPU NVIDIA RTX 4060, HD Audio e Killer/Intel.
- [ ] QA grafica Windows e DPI multipli (#95).

## Limite

Questo fix aumenta la resilienza dell'inventario PnP e impedisce diagnosi errate quando WMI è incompleto; **non** aggiunge nuovi cataloghi OEM né garantisce più aggiornamenti installabili. Il gap Driver Easy 8 vs OmegaDrive 2 resta aperto in #91.
