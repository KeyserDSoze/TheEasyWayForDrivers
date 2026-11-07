# OEM ZIP INF Evidence — fase Deep Inspection (#72)

Il componente `OemZipInfEvidence` parte dall'inventario già validato da `OemArchiveInspector`, poi legge **solo gli INF** di ZIP locali, in memoria, senza scrivere file estratti su disco. È collegato al report testuale Discovery+ che mostra fino a 60 ID con la sezione e il percorso INF. Non interroga automaticamente un catalogo OEM né autorizza installazioni.

## Correzione dei percorsi ZIP Windows (#224)

Gli archivi OEM Windows possono contenere entry come
`Drivers\\NVIDIA\\display.inf`. L'indice ZIP le normalizza in
`Drivers/NVIDIA/display.inf` per i controlli di sicurezza, ma il nome
originale nella central directory non cambia. In precedenza i lettori
chiamavano `ZipArchive.GetEntry` con il percorso normalizzato e potevano
fallire prima del confronto PnP, anche per INF validi.

Ora un **indice condiviso dei file ZIP** associa il percorso normalizzato
all'entry originale per anteprima INF, confronto completo e ZIP annidati.
Il lookup è case-insensitive e conserva l'identità dell'entry: non cerca
mai per semplice nome file e non sceglie arbitrariamente fra duplicati.
Se `a\\b.inf` e `a/b.INF` compaiono insieme, l'archivio è rifiutato.
Restano attivi i controlli traversal, symlink, limiti dichiarati e ratio
di compressione; nessun eseguibile OEM viene avviato.

## Limiti attuali

- massimo **96 INF** per archivio, **4 MiB** per INF e **24 MiB** di INF totali;
- ZIP esterni fino a **4 GiB**; controllo di dimensioni, compressione, path,
  symlink, collisioni case-insensitive e massimo 2048 entry;
- lettura dei soli INF in memoria, senza estrarre SYS/DLL/EXE;
- supporto UTF-8, UTF-16 LE/BE con BOM e recupero conservativo della
  sola sintassi ASCII per INF ANSI legacy;
- anteprima ID generali limitata a 500, confronto mirato su PnP del PC
  anche oltre la 500esima voce con limite separato;
- confronto ZIP/CAB annidato fino a due livelli, limiti di estrazione
  separati, CAB/EXE gestiti soltanto nei flussi espliciti supportati;
- nessuna installazione, importazione PnP o download implicito.

Gli ID INF restano **dichiarazioni non autenticate**. Il lookup recuperato
non prova firma CAT, ranking SetupAPI, selezione Windows o applicabilità
definitiva; `IsInstallable=false` sempre. I test includono GPU NVIDIA,
Killer, Realtek, ZIP misti `\\`/`/`, due livelli, percorsi pericolosi e
collisioni normalizzate. Il collaudo Windows reale resta in #91 e #136.

## Successivi controlli necessari

1. Validare test e compilazione Windows in CI, poi QA reale (#5).
2. Confrontare ID dichiarati con dispositivi reali senza mai attribuire una compatibilità definitiva.
3. Valutare un preflight per modello OS/architettura con API Windows e verifica CAT separata (#46, #58).
4. Progettare un lettore CAB isolato e documentato, con limiti equivalenti prima di analizzarne qualsiasi payload.
