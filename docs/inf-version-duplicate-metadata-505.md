# Metadati [Version] INF duplicati e selezione CAT (#505)

## Problema
Un INF OEM in sola lettura puo dichiarare piu volte la stessa direttiva [Version].
Il precedente GetVersionField restituiva la prima riga e ignorava eventuali
contraddizioni successive: una CatalogFile.NTamd64 duplicata poteva far
selezionare una CAT locale differente da quella effettivamente intesa.
Anche Signature e DriverVer potevano essere interpretate a partire dalla
prima occorrenza, senza rilevare l'ambiguita.

## Protezione
- Per ciascuna chiave [Version] richiesta, si leggono tutte le dichiarazioni.
- Nessun duplicato (anche case-insensitive o identico) produce un valore attendibile.
- Una direttiva CatalogFile specifica ripetuta e dichiarata ma invalida:
  non autorizza fallback a CatalogFile.NT o CatalogFile generico.
- Signature duplicata richiede revisione manuale, senza forza Hardware ID.
- DriverVer duplicato resta non dichiarato ai fini di confronto versioni;
  l'evidenza PnP separata resta disponibile in sola lettura.
- Le direttive distinte conservano l'ordine arch-specific, NT, generic.

## Regressioni
InfVersionDuplicateMetadataTests include dichiarazioni duplicate con
diversi case e valori, fallback architettura, firma, DriverVer, ZIP locale e
planner CAT nella cartella. Una CAT presente non equivale a firma digitale
verificata, membership SYS/DLL o installabilita.

## Limiti
Il preflight non sostituisce SetupAPI, verifica Windows della firma o QA
su macchina reale. Restano aperte #128, #130, #136 e #85.
