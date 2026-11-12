# WUA→PnP e INF OEM: due righe per lo stesso DeviceId, vendor contraddittori (#385)

Una scansione WMI/SignedDriver può contenere righe duplicate della stessa istanza PnP con modelli vendor incompatibili, come NVIDIA/Realtek. La issue #383 ha già applicato una regola fail-closed a Home, inventario e handoff; mancavano la diagnostica WUA→PnP e il confronto di file OEM locali.

## Correzione

- WuaPnpAssociationAudit.Create filtra prima gli ID usando PnpDistinctTrustedDevices e non conta come hardware-esatto, CompatibleID o solo-modello i GUID correlati unicamente a una stessa istanza contraddittoria.
- Un contatore anonimo ExcludedAmbiguousPnpInstances e un messaggio spiegano che i casi scartati non dimostrano assenza di driver. Non espone DeviceId, seriale o Hardware ID.
- LocalOemDriverComparison.Compare applica la medesima policy prima di leggere associazioni INF e indizi review-only: pacchetti ZIP/CAB/folder annidati non producono versioni più nuove per identità ambigue.
- Il confronto OEM conserva i DeviceOrdinal della lista originale, quindi il dedup dei 24 archivi rimane corretto anche quando si scartano righe intermedie. Un device Killer sano può rimanere all'indice originale 0, 1 o 2.
- I DeviceId vuoti non producono una chiave di dedup e non possono causare una correlazione.

## Prove e limiti

I test sintetici verificano ordine invertito, GPU NVIDIA vs codec Realtek, versione INF potenzialmente newer, confronto su due archivi, ordinale stabile, duplicati concordi e dati privati esclusi dai report.

Non vengono interrogati altri cataloghi, non si aggiungono GUID né si autorizzano installazioni. La firma CAT resta non verificata; QA hardware Recoil 16 (#91/#136), Windows E2E (#85) e Authenticode (#283) ancora aperti.