# OmegaDrive — INF Windows multilinea e valore probatorio dei Compatible IDs (#168)

## Difetto individuato

Dopo la v0.0.79, OmegaDrive riusciva a correlare anche ID presenti dopo il 500° modello di un INF. Restavano due difetti importanti:

1. **Direttive spezzate su più righe:** Microsoft consente `\\` come continuazione esplicita delle direttive INF. Il lettore interpretava le righe fisiche separatamente, perciò un `[Manufacturer]` lungo o la continuazione della voce `[Models]` potevano nascondere Hardware ID del PC, producendo falsi zero nei confronti ZIP/cartella OEM.
2. **Compatible ID dichiarato dal produttore elevato a ID principale:** Microsoft specifica `device-description=install-section,[hw-id][,compatible-id...]`. Il primo ID è l'Hardware ID del modello INF, gli ID successivi sono compatibili. Il vecchio motore assegnava forza **2 / Hardware ID esatto** a *qualunque* campo se appariva nell'elenco `HardwareIds` del PC. Questo poteva far sembrare un pacchetto OEM genericamente compatibile un candidato sicuramente migliore perché aveva una versione numerica più elevata.

Fonti Microsoft:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/general-syntax-rules-for-inf-files
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/inf-models-section

## Comportamento implementato

- Nuovo parser condiviso **`InfLogicalLines.Read`** per commenti INF al di fuori di stringhe in virgolette e token `%...%`, e continuazioni esplicite con barra inversa; ignora le righe fisiche vuote, limita le direttive logiche a 32 KiB, massimo 64 continuazioni, 1 MiB INF. Le direttive rimaste incomplete non vengono accettate come prova.
- `InfModelEvidence.ReadDeclaredIds` (anteprima) e `ReadMatchingIds` (matching mirato sull'intero INF) usano le stesse righe logiche, rispettando limiti di memoria e la whitelist di bus PnP già introdotti.
- `InfModelEvidence.DeclaredId` registra `IsPrimaryHardwareId` (default `true` per retrocompatibilità con i test chiamanti): solo il **primo ID dopo la sezione Install** è primario, tutti gli altri sono alias compatibili. Duplicati sono rilevati per *sezione + ID + ruolo*; un alias visto prima non può cancellare un ID primario successivo.
- Il **matching di forza 2** richiede che l'ID primario dell'INF corrisponda ad un **HardwareId del dispositivo Windows**. Qualsiasi altra associazione esatta (CompatibleId del dispositivo oppure ID compatibile dell'INF) ha **forza 1**. Non vengono introdotte associazioni per titolo, vendor o prefisso.
- `InfPlatformPreflight.Assess` usa la stessa distinzione e calcola la forza in relazione alla *sezione del modello pertinente all'architettura/build Windows*, evitando che un ID primario ARM64 attribuisca forza 2 a un match soltanto compatibile x64.
- `LocalOemDriverComparison` e `LocalOemFolderComparison` ereditano queste verifiche; un ID genericamente compatibile non è più presentato come `IsPotentialNewer` solo per il numero di versione.
- Nessuna fonte online aggiunta; nessuna installazione, staging, esecuzione di EXE OEM o modifica a Windows Update.

## Regressioni automatiche

- INF con continuazione `[Manufacturer]` e `[Models.NTamd64]`, GPU NVIDIA primaria e Killer compatibile.
- Confronto ZIP, cartella OEM ed evidenza PnP corretta per Realtek Intel Smart Sound, NVIDIA e Killer, con `DriverVer` solo informativo.
- INF con primary NVIDIA, alias Intel/Killer: quest'ultimo resta forza 1 e `IsPotentialNewer=false`.
- Un ID primario ARM64 non rafforza una corrispondenza compatibile x64; un alias duplicato prima del primary non lo nasconde.
- Commenti `;` dentro virgolette/`%token%` non spezzano la stringa; i backslash finali **dentro** virgolette non sono continuazioni di direttiva.
- Direttive troncate, oltre 64 continuazioni o 32 KiB e INF oltre 1 MiB falliscono in modo esplicito. Sezioni non dichiarate, vendor simili e file malformati non diventano driver confermati.
- [ ] CI Windows .NET 10 / Shared.Core + Service tests, WPF/IPC.
- [ ] QA Recoil 16 con pacchetti OEM originali, firme CAT/INF/SYS/DLL e ranking SetupAPI ancora aperti in #130 e #136.

**Limite esplicito:** un INF che corrisponde a un ID PnP non conferma disponibilità di un aggiornamento più recente. `IsInstallable=false`, `IsVerifiedCompatible=false`, `Aggiorna tutto` WUA e policy di sicurezza invariati. Il gap 2 vs 8 (#91) non è automaticamente risolto.
