# OmegaDrive — indizi driver da ricerche WUA parziali (#174)

## Il problema

L'API ufficiale Windows Update Agent (WUA) espone indipendentemente `ISearchResult.ResultCode`, `Updates` e `Warnings`. Se una scansione termina con errori parziali (`orcSucceededWithErrors = 3`) o con warning pur riportando `ResultCode = 2`, non è sicuro presentare `Updates` come una lista completa di aggiornamenti confermati per il PC.

Prima di questo intervento OmegaDrive scartava **l'intera lista senza mostrarne neppure i titoli**. Era giusto per la sicurezza di `Aggiorna tutto`, ma rendeva più difficile capire se Windows avesse almeno restituito una voce NVIDIA, Intel/Killer, Realtek o altro durante una fonte incompleta.

Fonti Microsoft:
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-isearchresult
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search

## Nuovo comportamento

1. La ricerca WUA `Type='Driver'` continua a produrre **aggiornamenti verificati** soltanto quando la fonte restituisce `ResultCode=2`, senza warning, e tutta la collezione COM è letta correttamente.
2. In una fonte con `ResultCode=3` oppure `2 + warnings`, OmegaDrive può leggere i *titoli* tramite il nuovo `WuaPartialResultReader.ReadAll`. Se un oggetto COM è illeggibile o la cancellazione interrompe l'enumerazione, **nessun titolo di quella fonte viene conservato**. `0/1/4/5/sconosciuto`, warning inaccessibili e più di 4000 voci sono trattati come falliti/non verificati.
3. I titoli vengono filtrati dal nuovo `WuaPartialDriverHintCollector`: GUID valido per deduplicare, fonte di ricerca ufficiale nota, titolo breve (max 200 caratteri) privo di controlli, percorsi, barre, caratteri bidirezionali pericolosi. Il GUID **non viene mostrato** nel dettaglio.
4. Il limite di visualizzazione è **32 indizi**; titoli provenienti da pass diversi con lo stesso GUID vengono contati una volta. Se lo stesso GUID è ritrovato da un pass WUA **completato**, l'indizio parziale non viene mostrato come seconda offerta.
5. `DriverSearchReport` trasporta questi indizi in un campo **separato e opzionale**, conservando la compatibilità IPC. `DriverSearchReport.Updates` resta il solo insieme di candidati driver verificati per la UI/installazione.
6. Nella Home, dentro il pannello avanzato OEM già collassato, compare **Driver da ricerche Windows incomplete · NON installabili** solo se esistono voci. Ogni riga indica origine e natura non verificata. Non viene creato alcun pulsante installa/aggiorna.
7. Il report copiabile `ScanCoverageText` esporta soltanto **conteggi aggregati** (visualizzati/oltre limite) e stato delle fonti. Non esporta titoli WUA, GUID, Hardware IDs, Device Instance ID, percorsi o messaggi di errore grezzi.
8. `QuickUpdatePolicy`, `WindowsDriverCandidateMerge`, `InstallCore`, `DriverUpdateInfo`, selezione device per ID, download e servizi OEM restano invariati.

## Cosa significa una voce

Un titolo trovato in un pass parziale è **solo un indizio pubblicato da WUA durante una ricerca incompleta**, non un'offerta installabile, un aggiornamento più recente, una corrispondenza PnP verificata o un pacchetto firmato. La disponibilità complessiva rimane sconosciuta quando tutte le fonti sono parziali o fallite.

La comparazione «2 driver OmegaDrive contro 8 voci Driver Easy» **non è risolta automaticamente**: questa modifica offre più evidenza diagnostica nelle scansioni parziali; sui PC con tutte le ricerche WUA completate l'elenco degli indizi parziali è normalmente vuoto.

## Verifiche

- [x] Unit test per stati WUA 2 pulito, 3, 2 con warning, 0/1/4/5 e sconosciuti.
- [x] Fake COM per enumerazione completa, COM failure a metà, cancellazione, GUID/titoli malformati e limite 4000.
- [x] Test Core per fonti whitelist, protezione titoli, limiti 32, dedup GUID, separazione dalla lista installabile, report aggregato e retrocompatibilità.
- [x] Contratto WPF statico: sezione collassata, binding separato, nessun pulsante installazione.
- [ ] CI Windows .NET 10, test Core/Service, WPF/IPC e release.
- [ ] QA Recoil 16 su fonti WUA realmente parziali: contare i titoli NVIDIA/Intel senza promuoverli ad aggiornamenti installabili, non condividere identificativi privati.
- [ ] QA WUA completamente riuscita: il nuovo pannello deve restare nascosto senza risultati parziali.
- [ ] Verifica pacchetti realmente firmati e compatibili con modello PC e SetupAPI continua in #91/#130/#136.

Riferimenti: #91, #100, #130, #136, #174.

## Selezione globale da pass WUA parziali (#336)

Il limite di 32 titoli sul primo pass nascondeva NVIDIA/Killer/Realtek di ricerche successive. Ora OmegaDrive raccoglie tutti gli indizi del solo tipo Driver da pass WUA parziali completati a livello di enumerazione COM, con massimo 6 pass da 4000 record ciascuno. Qualunque errore o overflow non pubblica un pass incompleto. Dopo le fonti, rimuove i GUID gia confermati da pass puliti e sceglie 32 titoli con round-robin fra produttori e fra fonti dello stesso produttore. Killer resta distinto da Intel. Le omissioni contano solo GUID distinti non confermati. Nessun titolo parziale diventa un aggiornamento verificato, un suggerimento installabile o un nuovo GUID WUA. Il report esportato resta anonimo e la QA Recoil 16 e ancora richiesta.
