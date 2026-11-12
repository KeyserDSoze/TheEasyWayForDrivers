# WUA Type=Driver: status fonte duplicati non verificati (#377)

Una ricerca WUA Type=Driver normale ha un solo esito per ogni pass del
`WindowsDriverSearchPlan`. In uno snapshot IPC legacy o malformato possono
invece comparire due righe per la stessa origine, per esempio
`Windows Update online: Completed` e `Windows Update online: Failed`.
Prima del fix, il filtro controllava solo se **almeno una** riga era
`Completed`, con il rischio di mostrare come verificati GUID e hardware
associati alla fonte contraddittoria.

Ora `WindowsDriverCandidateMerge` convalida una fonte soltanto con
**esattamente uno** status riconosciuto e `Completed`. I nomi sono
confrontati case-insensitive per impedire che varianti della stessa fonte
aggirino il veto. `HasCompletedKnownWuaPass` e `ConfirmedForReport`
usano la stessa regola; una diversa fonte autenticamente completata
continua ad essere utile e può contribuire i propri GUID, senza
importare ID PnP della fonte contraddittoria. Non cambia il numero
dei pass eseguiti né il servizio privilegiato di installazione.

`WuaPassStatusPresentation` segnala esplicitamente il conflitto di
stati senza ripetere `Completed` in modo fuorviante, e non visualizza
`Detail`, URL locali, Device Instance ID, Hardware ID o GUID.
Test regressivi coprono fonti ripetute in ordini diversi, varianti
maiuscole, errori `Failed`/`Partial`/`NetworkError`, due
`Completed` duplicate, pass avanzati, GUID NVIDIA/Killer da fonti
distinte, copia anonima e isolamento dalla selezione `Aggiorna tutto`.

La correzione è conservativa e riguarda prova di provenienza, UI e
report, non nuovi driver trovati. Rimangono necessari il confronto
8-vs-2 sul Recoil 16 (#91/#136), l'E2E Windows reale (#85) e la firma
Authenticode (#283).
