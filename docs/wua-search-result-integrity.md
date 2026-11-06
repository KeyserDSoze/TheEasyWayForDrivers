# OmegaDrive — integrità del risultato WUA (issue #154)

## Bug
`IUpdateSearcher.Search()` può restituire un oggetto `ISearchResult` anche quando la ricerca non è terminata correttamente. Il servizio Windows di OmegaDrive leggeva `Updates` senza controllare `ISearchResult.ResultCode` né `Warnings`, marcando come completata una fonte che Windows aveva eseguito con errori. Questa anomalia poteva produrre conteggi di driver erroneamente considerati esaustivi e dare un'impressione sbagliata sull'assenza di candidati NVIDIA/Intel.

Documentazione Microsoft:
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-isearchresult
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search

## Comportamento nuovo

| Codice WUA | Warning | Esito OmegaDrive | Uso delle voci |
| --- | --- | --- | --- |
| 2, Succeeded | 0 | Completed | Sì, dopo lettura integra della fonte |
| 2, Succeeded | >=1 | Partial | No, non convalidate |
| 3, SucceededWithErrors | qualunque | Partial | No, non convalidate |
| 0,1,4,5 o sconosciuto | qualunque | Failed | No, non convalidate |
| proprietà COM non leggibili | non verificabile | Failed | No, non convalidate |

`DriverSearchPassAudit` impedisce il commit di candidati da un pass parziale. I risultati di *altre* fonti WU/MU pulite rimangono nel report. `DriverSearchReport.IsPartial` impedisce `Aggiorna tutto` se una ricerca **driver** è parziale. Eventuali problemi WUA `Type=Software` vengono riportati separatamente e non invalidano una ricerca `Type=Driver` sana.

In fase di installazione `AddSelectedUpdatesFromSearch` accetta soltanto risultati completi. La query del servizio WUA configurato, se fallisce/è parziale, non blocca la possibilità di una ricerca pulita e successiva tramite Windows Update o Microsoft Update già registrato. Il GUID selezionato deve ancora essere ritrovato dalla ricerca completa e passa per le stesse policy WUA, senza introdurre fonti OEM o download arbitrarî.

## Privacy e limiti
Si legge soltanto il numero di warning, mai il loro contenuto o stringhe identificanti. Il report copiabile segnala `ricerca con avvisi/errori; risultati non convalidati` senza esporre Instance ID, Hardware ID, seriali o nomi file. Nessuna modifica a query WUA, servizi, policy, firme, BIOS/firmware o installatori.

**Non implica più nuovi driver**: il bugfix rende verificabile perché la ricerca possa essere incompleta, requisito per diagnosticare correttamente il gap 2 vs 8 di #91.

## Test
- Unit test codici 0–5 e sconosciuti, warning con codice 2, codici 3 con/senza warning.
- Finti oggetti COM: lettura pulita, proprietà non disponibile, failure senza consultare warning, no enumerazione di Updates prima di validazione.
- Fonte driver parziale + completa: conserva update buoni, report IsPartial e blocco bulk; software parziale isolato.
- Report aggregato con messaggio `Partial`, nessun ID PnP esportato.
- CI Windows .NET 10 build, xUnit Core/Service, test contratto IPC e static WPF.
- [ ] QA runtime Recoil 16: verificare ResultCode/WarnCount e confrontare fonti che rispondono con Driver Easy (#91, #136), senza log privati.

