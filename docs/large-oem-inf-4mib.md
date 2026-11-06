# OmegaDrive — ampliamento INF OEM fino a 4 MiB (#170)

## Problema

Nella v0.0.80 il confronto di pacchetti locali si interrompeva quando **un singolo INF superava 1 MiB**, anche se il file apparteneva a un archivio valido. Lo stesso limite era presente in tutte le fasi: anteprima ZIP, parser `Manufacturer/Models`, preflight OS/arch, espansione CAB, cartella OEM e analisi manuale di un INF. Un file grande poteva così essere escluso **prima ancora** di cercare l'Hardware ID effettivo di NVIDIA, Intel/Killer o Realtek.

La documentazione Microsoft descrive l'INF come uno dei componenti del pacchetto driver, ma non prescrive la soglia applicativa di 1 MiB: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/components-of-a-driver-package

## Cosa cambia

- **Limite per singolo INF: 4 MiB**, centralizzato in `OemZipInfEvidence.MaxInfBytes` e riutilizzato dal decoder, dal parser INF, dal preflight e dai percorsi di confronto ZIP, CAB e cartella.
- Nel controllo di **INF singolo** dalla UI, il caricamento usa `OemInfTextDecoder.ReadBounded(FileStream)` con la stessa decodifica rigorosa UTF-8 / UTF-16 LE/BE / ANSI ASCII-safe già utilizzata nei pacchetti OEM; non viene più usato `File.ReadAllText` con codifica implicita.
- Il controllo SHA-256 locale utilizza il nuovo limite prima dell'analisi e rimane **evidenza di integrità, non di autenticità**.
- Restano invariati i limiti per la sicurezza: **24 MiB di INF complessivi**, massimo 96 file INF, validazione ZIP con rapporto di decompressione, path traversal e symlink respinti, cap 500 voci di anteprima, matching completo dei soli ID Windows (max 4096), max 2048 dispositivi, 32 KiB/riga logica e 64 continuazioni.
- I file oltre 4 MiB restano **rifiutati esplicitamente**, non classificati come "nessun driver trovato".
- Tutto il percorso è **read-only**: non estrae o esegue driver OEM negli archivi ZIP, non installa driver o BIOS e non modifica Windows Update.

## Test automatici

- [x] INF sintetico di oltre 1 MiB contenente più di 620 modelli prima del target PnP NVIDIA, con lettura completa e preflight x64.
- [x] ZIP OEM NVIDIA/Killer con INF di grandi dimensioni, forza Hardware ID corretta, `DriverVer` solo *indizio*, nessuna installabilità.
- [x] Cartella OEM estratta con INF superiore al vecchio limite.
- [x] INF oltre 4 MiB rifiutati da decoder, parser e preflight, senza superare il budget.
- [x] Identificativi NVIDIA simili ma non uguali rimangono non corrispondenti.
- [ ] CI Windows .NET 10, test Core+Service, WPF/IPC e release.
- [ ] QA sul Recoil 16 con INF autentici: dimensioni, quantità match recuperati e verifica CAT/SetupAPI (issue #130 e #136).

## Cosa NON prova

Questo cambiamento può recuperare **corrispondenze in pacchetti OEM locali**, non offre automaticamente nuovi download online e non certifica che un driver sia adatto al notebook. Il gap «Driver Easy 8 vs OmegaDrive 2» resta aperto (#91); `IsInstallable=false`, `IsVerifiedCompatible=false` e la selezione di `Aggiorna tutto` restano invariati.

Refs: #91, #100, #126, #130, #136, #170.
