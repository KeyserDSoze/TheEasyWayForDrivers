# OEM Package Inspector — primo inventario statico (#68)

Il nuovo `OemArchiveInspector` è una libreria del Core **non ancora collegata all'interfaccia desktop**. Riceve uno stream leggibile e seekable, riconosce firme/header elementari e non fa download, estrazione o esecuzione.

## Supporto

| Formato | Operazione |
|---|---|
| ZIP (header locale PK) | Legge la directory degli elementi con `ZipArchive`, identifica estensioni INF/CAT/SYS, nessun accesso al contenuto decomprimendo |
| CAB (MSCF) | Riconoscimento superficiale, nessun elenco né estrazione |
| MSI (Compound File) | Riconoscimento superficiale, nessun elenco né esecuzione |
| EXE (MZ/PE candidato) | Riconoscimento superficiale, nessuna esecuzione |
| Altro | Unknown, revisione manuale |

## Limiti e sicurezza

- ZIP: al massimo 2048 elementi, 1 GiB dimensione input, 512 MiB per elemento e 2 GiB di dimensioni non compresse **dichiarate**, rapporto di compressione dichiarato fino a 200:1.
- Rifiuta path assoluti, UNC, lettere di unità, traversal, componenti vuote o `.`, elementi duplicati senza distinzione maiuscole/minuscole e symlink ZIP Unix.
- I valori dichiarati negli header ZIP non sono necessariamente affidabili: questo modulo non estrae i byte, quindi **non certifica l'assenza di zip bomb** o l'autenticità dei file.
- Un `.inf` trovato in uno ZIP non è stato letto né confrontato con Hardware ID o CatalogFile; `.cat` non implica firma verificata.
- I riconoscimenti CAB/MSI/EXE identificano solo l'intestazione iniziale; non costituiscono una validazione completa del formato.
- `IsInstallable=false`, `ContentsAuthenticated=false` e `RequiresManualReview=true` per ogni risultato.

## Successive fasi

Collegamento UI con consenso esplicito per file locali, analisi CAB con API documentate e limiti separati, controlli affidabili CAT/INF, QA Windows 11 reale (#5). I formati EXE self-extracting e MSI restano **non ispezionati** fino a quando è definito un sandbox / parser sicuro dedicato.

## Interfaccia Discovery+ (#70)

Il pulsante **Ispeziona archivio OEM locale** apre il selettore file e chiede conferma prima dell'ispezione. Solo file locali scelti esplicitamente dall'utente vengono letti in modalità read-only. Il report elenca tipo formato, conteggi INF/CAT/SYS, fino a 80 riferimenti ZIP e gli avvertimenti. La presenza di un file non implica autenticazione, compatibilità o installabilità. File troppo grandi, reparse point e input non riconoscibili non ottengono alcun attestato positivo.

Questa UI non usa i risultati per proporre download o installazione. CAB/MSI/EXE rimangono non enumerati. La verifica su Windows reale (#5) è ancora necessaria.
