# Aggiorna tutto: dedup GUID e veto trusted cross-source (#367)

## Contesto

OmegaDrive normalmente consolida le offerte WUA con
\`WindowsDriverCandidateMerge\`, che normalizza l'UpdateID in forma GUID D,
conserva i veto \`BrowseOnly\` e non si lascia contaminare da fonti OEM.
Però l'API \`QuickUpdatePolicy.AllOrdinaryIds\` usata direttamente dalle
azioni Desktop \`Aggiorna tutto\` e \`Aggiorna selezionati\` filtrava
**prima** con \`IsEligible\` e deduplicava solo per stringa
\`OrdinalIgnoreCase\`. Snapshot IPC legacy o chiamanti che passano righe
WUA non consolidate potevano quindi selezionare lo stesso GUID scritto
D/B/N/P più volte o perdere il veto da un'altra fonte WUA attendibile.

## Soluzione fail-closed

La selezione bulk ora:
1. scarta i record privi di GUID valido o senza origine WUA riconosciuta;
2. raggruppa i record tramite il valore di \`Guid.Parse(Id)\`, non tramite
   il testo dell'ID;
3. richiede **almeno un** record realmente \`IsEligible\`;
4. rifiuta l'intero GUID se **qualunque** record WUA trusted dello stesso
   gruppo segnala \`IsOptional\` oppure Firmware/BIOS/UEFI;
5. restituisce un solo GUID canonico D per gruppo, nell'ordine di prima
   apparizione dei GUID attendibili.

Una fonte OEM/manuale sconosciuta non può creare né veto né
autorizzazioni. Non sono stati cambiati \`IsEligible\`, la scelta del
driver migliore per dispositivo, il recheck nel servizio privilegiato,
download/installer, o le sorgenti interrogate. È una protezione
**supplementare**, non la prova di un'installazione errata precedente.

## Test e limiti

Test xUnit su quattro formati D/B/N/P, conflitti trusted
Recommended/BrowseOnly, Firmware/UEFI, spoofing OEM, advanced-only,
GUID distinti e legacy ID non-GUID. Il risultato bulk coincide con la
decisione ottenuta dopo il merge WUA ufficiale.

I risultati effettivi della ricerca NVIDIA/Intel/Realtek sul Recoil 16
(#91/#136), il ranking, il confronto OEM e la firma Authenticode
(#283) richiedono verifiche separate.
