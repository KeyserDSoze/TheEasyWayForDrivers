# WUA: più Hardware ID per un unico aggiornamento (#147)

## Correzione
Prima OmegaDrive leggeva solo `IWindowsDriverUpdate.DriverHardwareID`. Microsoft espone anche `IWindowsDriverUpdate4.WindowsDriverUpdateEntries`, che contiene più `IWindowsDriverUpdateEntry.DriverHardwareID` per lo **stesso aggiornamento**.

Fonti Microsoft:
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iwindowsdriverupdate4-get_windowsdriverupdateentries
- https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nn-wuapi-iwindowsdriverupdateentry

Il servizio ora legge in sola lettura un massimo di 128 entry, normalizza e deduplica gli ID, scarta testo malformato e conserva l'ID principale quando l'interfaccia COM opzionale non è disponibile. Un campo opzionale `DriverUpdateInfo.AdditionalHardwareIds` attraversa l'IPC JSON; i vecchi payload rimangono supportati.

Il matcher verifica tutti gli ID dichiarati con uguaglianza esatta (prima Hardware ID e poi Compatible ID), evitando inferenze da nome o prefisso. Il merge tra pass WUA/Microsoft Update per **Update ID GUID** preserva le associazioni aggiuntive senza creare nuovi aggiornamenti e senza promuovere i candidati avanzati.

## Vincoli di sicurezza
Non cambia alcuna query WUA, origine consentita, policy installazione, conferma, firmware o BIOS. Una correlazione ID non è certificazione di installabilità, né prova dell'esistenza di una versione più recente. Non vengono interrogate API non documentate NVIDIA App/Intel DSA e nessun driver OEM viene scaricato automaticamente.

## QA
- [ ] Test COM fittizi: 0/1/N entry, duplicati, input malformato, eccezioni e limite.
- [ ] Test matcher e merge: hardware/compatible ID, falsi prefissi, un GUID = un update.
- [ ] Test IPC serializzazione con e senza campo opzionale.
- [ ] GitHub Actions Windows: restore/build/xUnit + IPC e WPF contract.
- [ ] Sul PC reale: verificare associazioni offerte da WUA/Microsoft Update per NVIDIA e Intel, senza condividere hardware ID/seriali.
- [ ] Il gap Driver Easy 2 vs 8 (#91, #136) rimane aperto: possono aumentare i **dispositivi correlati**, non il numero di aggiornamenti WUA disponibili.

Riferimenti: #22 #91 #100 #111 #136 #147.
