# OEM ZIP INF Evidence — fase Deep Inspection (#72)

Il componente `OemZipInfEvidence` parte dall'inventario già validato da `OemArchiveInspector`, poi legge **solo gli INF** di ZIP locali, in memoria, senza scrivere file estratti su disco. È collegato al report testuale Discovery+ che mostra fino a 60 ID con la sezione e il percorso INF. Non interroga automaticamente un catalogo OEM né autorizza installazioni.

## Limiti

- massimo 32 INF per archivio;
- al massimo 1 MiB decompresso per INF e 8 MiB totali;
- verifica continuativa dei byte effettivamente letti, non soltanto metadati ZIP;
- supporto UTF-8 rigoroso e UTF-16 LE con BOM; encoding non risolti mostrati come warning, non come prove;
- nessuna estrazione su filesystem, esecuzione, importazione PnP o download;
- CAB, MSI, EXE non vengono aperti.

Gli Hardware ID ricavati dai modelli INF sono **dichiarazioni non autenticate**. Il lettore non applica il ranking Windows SetupAPI, non prova che quel modello INF sia applicabile all'OS corrente e non esamina catena di firma o membership CAT. `IsVerified=false`, `IsInstallable=false` sempre.

## Successivi controlli necessari

1. Validare test e compilazione Windows in CI, poi QA reale (#5).
2. Confrontare ID dichiarati con dispositivi reali senza mai attribuire una compatibilità definitiva.
3. Valutare un preflight per modello OS/architettura con API Windows e verifica CAT separata (#46, #58).
4. Progettare un lettore CAB isolato e documentato, con limiti equivalenti prima di analizzarne qualsiasi payload.
