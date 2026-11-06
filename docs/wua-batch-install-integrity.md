# OmegaDrive — integrità batch Windows Update (#162)

## Perché serviva

La v0.0.76 considerava i codici WUA `2=orcSucceeded` e `3=orcSucceededWithErrors` equivalenti per download e installazione. Microsoft chiarisce che il valore 3 significa **completamento con errori, potenzialmente incompleto**, non riuscita totale. Il servizio non interrogava i risultati per singolo aggiornamento e consentiva installazioni anche quando nella ricerca WUA aggiornata erano rimasti solo alcuni GUID precedentemente selezionati.

Documentazione Microsoft:
- [OperationResultCode](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/ne-wuapi-operationresultcode)
- [IDownloadResult.GetUpdateResult](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-idownloadresult-getupdateresult)
- [IInstallationResult.GetUpdateResult](https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iinstallationresult-getupdateresult)

## Nuova procedura (fail-closed)

1. **Recheck selezione:** prima del download valida una lista di GUID WUA (1–256 ID distinti) e cerca tutti gli ID tra le sole fonti WUA già autorizzate. Se uno manca, **non** avvia alcun download o installazione. La UI deve eseguire una nuova scansione per una nuova selezione. Gli ID già trovati in un altro servizio non si duplicano. Cancellazioni `OperationCanceledException` non sono più catturate nella ricerca WU diretta.
2. **Snapshot atomico della fonte:** le voci ottenute da ogni ricerca GUID WUA vengono memorizzate temporaneamente e aggiunte solo dopo la completa enumerazione; un errore COM nel pass non rende verificati risultati letti a metà. I GUID sono marcati acquisiti **dopo** accettazione EULA e `UpdateCollection.Add`, mai prima.
3. **Download:** `orcSucceeded=2` sull'intero batch **e `2` per ogni** `IDownloadResult.GetUpdateResult(i)` sono necessari per richiamare `BeginInstall`. Codici 3,4,5,0,1/sconosciuti, proprietà mancanti, conteggi diversi o eccezioni COM significano download non confermato e **nessuna installazione** da questa richiesta.
4. **Installazione:** dopo `EndInstall` legge `IInstallationResult.ResultCode` e ogni `GetUpdateResult(i)`. `Succeeded=true` solo se **tutti** risultano 2 e il risultato globale è 2. Se parziale o non leggibile, il servizio riporta **esiti individuali confermati / non confermati** e chiede una nuova scansione; non fa rollback automatici perché alcuni driver potrebbero già essere stati modificati.
5. **Riavvio:** preserva `RebootRequired` globale o dei singoli driver quando Windows lo comunica, **anche con esito parziale**. La Home non mostra più «installazione completata» quando è richiesta una ripartenza a seguito di errori.
6. **Privacy:** i messaggi e l'outcome di installazione contengono solo conteggi e avvertenze, non HRESULT, Update IDs, Hardware IDs, nomi/seriali o testo grezzo COM. La serializzazione `DriverInstallResult` e l'API desktop–servizio rimangono compatibili.

## Verifiche

- [x] Finti oggetti COM per risultati globali 0–5 e sconosciuti.
- [x] Download globale 2 con item 2/3/4/5/1 e batch 3 con tutti gli item 2: nessun falso successo.
- [x] Installazione parziale con uno o più update riusciti, reboot globale o per-update, API COM incompleta e conteggi incoerenti.
- [x] Gate selezione GUID: 1 di 2 mancanti, duplicati, ID estraneo, GUID non valido, batch vuoto o oltre 256.
- [x] Cancellazione durante verifica item.
- [ ] CI Windows: compilazione .NET 10, test Core/Service, contratti IPC e WPF.
- [ ] QA manuale con ambiente Windows **di test**, update WUA realmente controllabili, evidenza di batch partial e reboot. Non provare errori d'installazione sul notebook quotidiano.

## Limiti e prossimi sviluppi

Questa è una correzione di sicurezza e affidabilità dell'**installazione WUA**, non un nuovo motore per trovare pacchetti OEM: non aumenta direttamente le offerte NVIDIA/Intel, non modifica driver ranking, firme, firmware, BIOS, Windows Update policy o registrazione di servizi. Il benchmark Driver Easy 2 vs 8 resta #91, con QA reale #136. La ricerca di nuove fonti ufficiali per-device deve mantenere un workflow separato e verificabile (#22, #100, #130).
