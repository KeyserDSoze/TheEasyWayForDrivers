# CAB: confine fra tabella CFFILE e primo blocco CFDATA (#524)

## Difetto
Il parser dell'indice CAB OEM, in sola lettura, verificava che
\`coffCabStart\` delle CFFOLDER fosse compreso fra \`coffFiles\` e
\`cbCabinet\`. Il parser delle CFFILE e dei nomi terminati da NUL
poteva però leggere fino a \`cbCabinet\`, anche quando la prima
CFDATA aveva inizio prima della fine della tabella. Un cabinet
malformato poteva trasformare byte dichiarati come dati compressi
in metadati INF pubblicati nel report.

## Correzione
- Memorizza il **minimo** \`coffCabStart\` fra le CFFOLDER (anche
  quando una cartella successiva inizia prima della prima).
- Il record fisso CFFILE di 16 byte deve concludersi entro quel limite.
- Il nome null-terminated deve contenere il terminatore **prima**
  dell'offset del primo blocco CFDATA, mai dentro la regione
  compressa. Il terminatore nell'ultimo byte della tabella è valido.
- Mantiene quote e controlli precedenti, CAB con CFHEADER riservato,
  versione MSCF, nomi sicuri e formati supportati.
- Nessuna lettura o decompressione di CFDATA, nessun EXE avviato
  e nessuna autorizzazione installazione driver.

## Regressioni
\`OemCabFileTableBoundaryTests\` genera CAB sintetici: CFFILE che
attraversa l'inizio dei dati, NUL rubato a CFDATA, secondo CFFILE
parzialmente sovrapposto, due CFFOLDER con data offset diversi,
oltre a CAB valido con uno/due INF e terminatore esattamente al
confine. Gli errori non includono path, seriali o ID hardware.

I test QA su un CAB PCSpecialist reale e la convalida firma CAT
sono tracciati in #122/#124/#128; nessun file OEM originale
è stato analizzato durante questa modifica.
