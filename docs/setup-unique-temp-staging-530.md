# Setup: staging temporaneo non prevedibile (#530)

## Problema
Prima di estrarre il payload ZIP, `SetupEngine` creava
`%TEMP%/TheEasyWayForDrivers/<GUID>`. Il parent condiviso
`TheEasyWayForDrivers` è prevedibile e il codice precedente lo
attraversava con `Directory.CreateDirectory` **prima** delle verifiche
sui reparse point dell'archivio (#528).

## Correzione
Il setup utilizza `SetupStagingDirectory.Create()`, che invoca
`Directory.CreateTempSubdirectory("OmegaDrive-Setup-")`.
Ogni invocazione crea una cartella unica **direttamente** sotto Temp.
La pulizia in `finally` e le verifiche ZIP/PE/versione precedenti
rimangono invariate. I test `SetupStagingDirectoryTests` coprono
identità dei path, creazione distinta e normale estrazione Service/Desktop.

## Mitigazione prima della scrittura iniziale (#561, 18 novembre 2026)
Il setup ora controlla gli antenati di Temp **prima** della creazione
staging, verifica il nuovo percorso e lo controlla nuovamente prima
di aprire `payload.zip` con `FileMode.CreateNew`. Il file non viene
sovrascritto se già esiste. Test: creazione normale, parent simbolico
già presente, directory mancanti senza effetti collaterali.

I controlli rimangono path-based e NON impediscono uno swap concorrente
tra controllo e apertura. ACL privata, handle Win32 e test delle race
restano da fare in #529.

## Limiti
Questa modifica elimina il parent aggiuntivo e prevedibile, ma non
garantisce una ACL esclusiva né previene una sostituzione concorrente
di un parent da parte di un processo già autorizzato. Gli hardening
handle-bound e le prove di concorrenza restano in #529. La CI non
equivale a una validazione di installazione Windows su hardware reale
(#85), né fornisce una firma Authenticode (#283).
