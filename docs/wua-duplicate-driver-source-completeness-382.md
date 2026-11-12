# WUA: gli stati Type=Driver non convalidabili rendono parziale la ricerca (#382)

## Difetto rilevato nella v0.0.241

La verifica \`WindowsDriverCandidateMerge.ConfirmedForReport\` scarta già
tutti i GUID di una fonte ripetuta, inclusi due stati \`Completed\`
(case-insensitive). \`WuaPassStatusPresentation\` dichiara quelle fonti
non verificate. Ma \`DriverSearchReport.IsPartial\` considerava soltanto
\`Failed\`, \`Partial\`, ecc.: con una fonte sana e un'altra duplicata
\`Completed + Completed\`, il riepilogo Home e la lista vuota potevano
riportare la scansione come completa mentre scartavano i suoi risultati.

## Risoluzione

Il controllo condiviso \`WindowsDriverCandidateMerge.HasUnverifiablePassStatuses\`
rileva fonti Type=Driver non appartenenti ai nomi del piano WUA, fonti
ripetute anche con differenze maiuscole/minuscole e \`NotRegistered\`
impostato su una fonte diversa dalla sola \`Microsoft Update online\`.
\`IsPartial\` torna vero senza contare falsi driver o reinterpretare i
GUID già confermati da un'altra fonte completa.

L'unico \`NotRegistered\` Microsoft Update resta neutrale: non è una
query WUA tentata. Le ricerche Type=Software e cache WUA sono separate
e non degradano lo stato delle query driver riuscite.

La modifica è di **integrità della diagnostica e della semantica di
completezza**. Non aggiunge fonti, pacchetti, GUID o installazioni.
I test Windows simulati coprono pass Completed/Failed/duplicati,
provenienza, Home, lista vuota e protezione dati. Il QA offline/WSUS
e quello hardware Recoil 16 restano #92/#85 e #91/#136.
