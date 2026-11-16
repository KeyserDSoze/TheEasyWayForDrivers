# Setup Windows: collisioni file/cartella nell'archivio incorporato (#519)

## Difetto
Il preflight di `SetupPayloadArchive.Extract` controllava la presenza
degli eseguibili Service e Desktop, traversal, link simbolici, limiti
di dimensione e duplicati di percorso. Non distingueva però i file
ZIP dai loro antenati: `Service/runtime` come **file** e
`Service/runtime/child.dll` erano considerati due percorsi distinti.
In fase di estrazione, il secondo elemento richiedeva la creazione di
una cartella dove era stato appena scritto un file: errore **dopo**
la scrittura di una porzione dello staging.

## Correzione
- Durante il preflight viene raccolto anche l'insieme dei file ZIP,
  con confronto case-insensitive indipendente dalla piattaforma.
- Prima di creare qualsiasi cartella di staging, tutti i percorsi
  già validati vengono confrontati con ogni antenato: se un antenato
  è un **file** dello stesso ZIP, l'intero pacchetto è respinto.
- La verifica è indipendente dall'ordine delle entry, copre directory
  esplicite e implicite, i percorsi profondi e i due EXE obbligatori.
- Nessuna modifica a estrazione, limiti 4096 entry/512 MiB ZIP,
  protezioni su reparse/traversal, scrittura `CreateNew` o cancellazione.
- Nessun nome ZIP o percorso privato nei messaggi di errore.

## Regressioni
`SetupPayloadAncestorCollisionTests`: file/figlio con casing diverso,
cartelle esplicite e implicite, conflitti con gli EXE essenziali,
entrambe le permutazioni di ordine, preflight senza modifiche alla
destinazione, pacchetto annidato valido. La verifica riguarda il
**payload dell'installer di OmegaDrive**, non driver OEM o firme CAT.
La firma Authenticode e il collaudo dell'installer su Windows reale
restano nelle issue #283 e #85.
