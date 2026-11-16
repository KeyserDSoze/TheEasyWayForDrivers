# INF Models/Manufacturer: quote rigorose condivise con CopyFiles (#502)

## Difetto
Il lettore logico INF convalida le virgolette **bilanciate**, ma
`InfModelEvidence` usava ancora `String.Split(',')` per
`[Manufacturer]` e `[Models]`. Una dichiarazione con virgola
all'interno di una stringa quoted o una quote chiusa con testo extra
poteva produrre un primo ID NVIDIA/Intel/Killer apparentemente valido
anche se la riga INF completa non era interpretabile. Anche
`Trim('"')` poteva eliminare una quote **letterale escaped** e
creare un Hardware ID falso.

Questi risultati alimentano preflight, ZIP/CAB/cartella OEM e i
controlli CAT/INF pianificati.

## Correzione
- `InfCommaFields.TrySplit` è il **singolo** parser bounded di CSV
  INF usato da `InfDependencyPreview` e `InfModelEvidence`: la
  virgola delimita solo fuori da quote, `""` produce una vera quote,
  e sono respinte quote in mezzo a token o suffissi dopo quote.
- Il lettore Manufacturer/Models ora analizza **tutti** i campi della
  direttiva prima di materializzare un ID; sintassi malformata produce
  `InvalidDataException`, trattata come INF corrotto, non un match
  parziale. Limite 1024 campi per record, oltre ai già esistenti
  4 MiB per INF, 32 KiB per riga logica, preview 500 e scan 4096.
- Rimosso `Trim('"')` sui campi già decodificati, per impedire che
  quote escaped si trasformino in Hardware ID.
- Mantenute le regole primary/compatible (forza 2/1), la scansione
  multilinea, gli identificativi noti NVIDIA/Killer/Realtek e il
  selettore read-only CAT. Il parser SourceDisks/CopyFiles usa
  esattamente l'implementazione precedente, ora estratta e condivisa.
- Test su INF genuini e malformati, ZIP con INF corrotto + sano,
  cartella di audit con CAT sintetico NON firmato, quote escaped,
  comma quoted, overbudget, privacy e regressione CopyFiles.

## Limiti
Nessuna firma CAT attestata dai fixture, né rank SetupAPI o
applicabilità del driver dimostrata. Nessun download, staging,
installazione o maggiore copertura WUA dichiarata. QA fisico
#85/#128/#130/#136 e gap Driver Easy #91 restano separati.
