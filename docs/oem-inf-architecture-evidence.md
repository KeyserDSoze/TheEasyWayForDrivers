# OEM INF architecture preflight (#76)

L'analisi degli INF letti in memoria da ZIP locali ora mostra indizi sull'architettura dichiarata nelle sezioni Models: `NTamd64`, `NTarm64`, `NTx86`. Un confronto indica solo se l'architettura dichiarata coincide con quella del PC. Sezioni senza decorazione architetturale riconoscibile sono **Unknown**.

Le decorazioni aggiuntive di versione Windows, build, ProductType e SuiteMask **non sono interpretate**. Anche con architettura coincidente il preflight NON prova corrispondenza del dispositivo, selezione DDInstall da SetupAPI, validità CAT, integrità dei file né installabilità. Nessuna estrazione su filesystem, download o installazione.

QA Windows #5 e verifiche complete #46/#58 restano indispensabili.
