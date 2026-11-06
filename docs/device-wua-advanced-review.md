# OmegaDrive — mostrare i candidati WUA avanzati nella scheda del dispositivo (#178)

## Problema verificato

Nella v0.0.84, `MainWindow.ApplyUpdateMatches()` eliminava **tutti i candidati WUA avanzati e nascosti** *prima* del matching `DriverUpdateMatcher.FindMatches`. L'elenco generale degli aggiornamenti mostrava già quei GUID sotto il filtro **Avanzati**; tuttavia la scheda **Driver installati** e il suo messaggio di Discovery+ potevano affermare **nessun aggiornamento WUA correlato per ID**, anche quando la GPU NVIDIA, la scheda Intel/Killer o un componente Realtek aveva un Hardware ID effettivamente corrispondente.

Questo non è un bug di download o di catalogo, ma una **falsa assenza nella vista per dispositivo**. Un candidato WUA avanzato non è un aggiornamento automaticamente raccomandato.

## Modifiche

- Nuovo classificatore Core **`DeviceWuaMatchGroups.Create`**: chiama il matcher di Hardware ID/Compatible ID su **tutte** le voci Type=Driver della ricerca WUA completata, poi le divide in `Ordinary` (solo `QuickUpdatePolicy.IsEligible`) e `ReviewOnly` (avanzati, nascosti, firmware/BIOS o altra sorgente non eleggibile). Il GUID rimane univoco; se un GUID ha sia forma ordinaria sia avanzata prevale la forma ordinaria eleggibile.
- `MainWindow.ApplyUpdateMatches()` ora usa entrambe le collezioni. Il matcher rimane esatto (case-insensitive) e non confonde nomi simili con identità PnP.
- `DriverDeviceRow` conserva separatamente update ordinari e voci da verificare. Lo stato **WUA da verificare**, il titolo e il conteggio dei candidati avanzati compaiono nella scheda dispositivo quando esistono, con messaggio **NON automaticamente installabile**. Quando ci sono solo voci avanzate non mostra più «nessun aggiornamento WUA correlato».
- Il pannello avanzato nella scheda dispositivo è **nascosto** quando non ci sono risultati pertinenti. Il pulsante `Apri elenco da verificare` apre la scheda **Aggiornamenti disponibili** con il filtro Avanzati: non avvia alcuna installazione.
- Il filtro avanzato in `Aggiornamenti disponibili` segue ora `!QuickUpdatePolicy.IsEligible`, come il filtro della Home: comprende anche firmware e sorgenti non ammesse per il click unico, non solo `IsAdvancedCandidate` o `IsHidden`.
- `HasAvailableUpdate`, `PreferredUpdate`, selezione per-device e pulsante `Installa update` sono riservati **esclusivamente ai candidati ordinari WUA**; l'handler installazione verifica nuovamente `QuickUpdatePolicy.IsEligible` prima del consenso.
- Non cambiano il numero di GUID di `DriverSearchReport.Updates`, la lista delle offerte generali, `Aggiorna tutto`, la ricerca WUA, il servizio, `InstallCore`, i download, la verifica firme o le policy Windows.
- Gli indizi `Type=Software` e i titoli da ricerche WUA **parziali** restano su tipi separati e non entrano nel matching per dispositivo.

## Test

- [x] Test Core: NVIDIA RTX 4060, Intel/Killer Wi-Fi e NVIDIA HD Audio con Hardware ID esatto o Compatible ID; risultati avanzati e nascosti restano `ReviewOnly`, senza driver ordinari inventati.
- [x] Stesso dispositivo con candidati ordinari + avanzati, entrambi mostrati nel gruppo giusto. GUID duplicati da pass diversi non gonfiano i conteggi e l'offerta ordinaria prevale.
- [x] Firmware/BIOS e sorgenti non autorizzate sono **solo consultazione**, senza selezione installabile; near-miss PnP e nome “NVIDIA” da soli non generano corrispondenze.
- [x] Contratto statico WPF: pannello dentro **Driver installati**, attivo solo su match avanzati, pulsante di navigazione al filtro Avanzati, installazione sempre gated da `QuickUpdatePolicy`.
- [ ] CI Windows .NET 10, test Core/Service, WPF/IPC.
- [ ] QA sul PCSpecialist Recoil 16: confrontare quanti dispositivi NVIDIA/Intel/Killer hanno match WUA **ordinari** rispetto a **avanzati/nascosti**, senza assumere nuovi pacchetti e senza condividere Device Instance IDs/seriali.

## Limiti espliciti

Una corrispondenza WUA per ID non garantisce che la versione sia più recente, che il pacchetto sia applicabile al notebook OEM, che abbia rank SetupAPI adeguato o che si debba installare. Questi controlli restano in #91/#130/#136. Questo intervento **non aumenta** il numero di pacchetti WUA restituiti né promette di colmare il divario 2 vs 8: corregge il collegamento fra offerte già restituite e dispositivo.

Rif. #91, #95, #99, #100, #130, #136, #178.
