# OmegaDrive — unificare i controlli d'installazione WUA (#180)

## Difetto risolto

In OmegaDrive v0.0.85, la Home e la scheda dispositivo avevano già la protezione `QuickUpdatePolicy.IsEligible`, ma il percorso **Aggiornamenti disponibili → Seleziona tutti → Scarica e installa selezionati** era rimasto separato:

- `DriverUpdateSelectionPolicy.ShouldSelectByDefault` preselezionava tutte le voci non Hidden e non Advanced, persino firmware/BIOS e fonti sconosciute.
- `SelectAllUpdatesButton_Click` selezionava anche risultati nascosti e avanzati.
- `InstallSelectedButton_Click` inoltrava gli ID di tutti i checkbox selezionati senza riesaminare la policy.
- Il servizio faceva un **nuovo search a criteri ampi** `IsInstalled=0 and Type='Driver'` con `IncludePotentiallySupersededUpdates=true` prima del download. Un GUID nascosto o avanzato poteva superare la seconda verifica.

L'errore poteva creare un percorso di installazione che aggirava il comportamento dichiarato della Home («solo driver Windows Update ordinari»).

Microsoft documenta sia `IsHidden=0` sia la ricerca di aggiornamenti sostituiti in `IUpdateSearcher.Search`: https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search .

## Modifiche

1. **Un'unica policy di selezione:** `DriverUpdateSelectionPolicy.ShouldSelectByDefault` chiama `QuickUpdatePolicy.IsEligible`, che esclude `IsHidden`, `IsAdvancedCandidate`, firmware/BIOS/UEFI per testo del titolo o classe, GUID non validi e fonti WUA sconosciute.
2. **Checkbox in sola consultazione per review-only:** `SelectableDriverUpdate.IsSelected` consente `true` solamente con `CanQuickInstall`. Anche se un'azione o un binding tenta di selezionare un item non idoneo, il setter lascia `false`.
3. **Scheda dettagli:** `Seleziona tutti` seleziona soltanto i candidati ordinari, `InstallSelectedButton_Click` estrae gli ID con `QuickUpdatePolicy.AllOrdinaryIds`; prima del contatto con il servizio, `InstallUpdatesCoreAsync` rifiuta batch vuoti, duplicati, GUID che non appartengono ai candidati eleggibili della scansione corrente.
4. **Trust boundary indipendente nel servizio:** `WindowsUpdateDriverProvider.AddSelectedUpdatesFromSearch` esegue il recheck solo con `RecommendedCriteria = IsInstalled=0 and IsHidden=0 and Type='Driver'` e `IncludePotentiallySupersededUpdates=false`, anziché cercare fra le offerte avanzate. Ogni candidato viene ulteriormente verificato con `WuaInstallRecheckPolicy.CanStage`: GUID valido, stesso ID richiesto, `IsHidden=false`, titolo valido, nessun firmware/BIOS/UEFI nel titolo/classe. Solo allora può entrare nella collezione WUA e raggiungere EULA, download e installazione.
5. **All-or-nothing:** il gate WUA esistente `WuaBatchSelectionGate.IsComplete` richiede che **tutti** i GUID del batch siano stati ricontrollati; un item sparito oppure non eleggibile interrompe tutto prima del download.
6. Nessuna modifica a discovery, matching PnP, cataloghi ufficiali, quantità di aggiornamenti disponibili, pacchetti OEM, ranking SetupAPI, firma CAT o firmware vendor. **I driver avanzati restano visibili nel filtro Avanzati e nella scheda device come voci da verificare, ma mai installabili attraverso i pulsanti di OmegaDrive.**

## Test e gate

- [x] Test di policy: ordinario WUA e optional selezionabili; hidden, advanced, titolo BIOS/UEFI, classe Firmware, fonte OEM falsa, GUID non valido non selezionabili per default.
- [x] Test di servizio: la ricerca di installazione è `RecommendedCriteria`, non broad, `IncludePotentiallySuperseded=false`; per-driver GUID e class/title/IsHidden validati prima di EULA/download.
- [x] Contratto statico WPF: verifica che l'handler legacy InstallSelected, SelectAll, il setter dei checkbox, il gate finale del client e il recheck in servizio utilizzino le policy.
- [ ] CI Windows .NET10, 900+ Core/Service xUnit, build WPF/IPC.
- [ ] PR merged solo con CI verde, release installer win-x64 e checksum SHA-256.
- [ ] QA reale Recoil 16 (#136): selezionare e tentare di installare **solo in modalità di prova senza confermare l'installazione** candidati ordinari vs review-only; per advanced/firmware tutte le azioni devono restare disabilitate. Non serve installare BIOS, firmware o driver terzi per la verifica.
- [ ] Separatamente, issue #91 continua ad affrontare il divario tra due offerte WUA e gli otto risultati del tool di terze parti.

Fonti: #91, #130, #136, #178, #180.
