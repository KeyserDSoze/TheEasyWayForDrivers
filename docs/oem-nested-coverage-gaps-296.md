# Bundle OEM: omissioni annidate visibili dopo il riepilogo (#296)

## Errore precedente
La v0.0.155 sa leggere ZIP locali contenenti EXE con overlay ZIP
standard, ma un ZIP composto **soltanto da MSI/MSIX/7z/RAR/ISO**
veniva trattato come privo di figli supportati: `0 INF`, senza
segnalazione della copertura incompleta. Inoltre la combinazione di
più ZIP cancellava le ragioni di esclusione degli EXE proprietari
salvando soltanto il booleano `HasUninspectedNestedArchives`. La
schermata PCSpecialist usava il solo contatore dei programmi esterni
e poteva indicare «0 pacchetti proprietari non analizzati» mentre gli
EXE **dentro** i ZIP erano stati scartati.

## Correzione
- In tutti i ZIP già convalidati vengono **conteggiati ma mai aperti**
  MSI, MSIX, 7z, RAR, ISO (il limite di entry ZIP è quello già in uso).
  Vale anche quando non vi è alcun INF, ZIP/CAB o EXE ispezionabile.
- `LocalOemDriverComparison.Report` trasporta esclusivamente tre
  interi anonimi opzionali (default 0), senza nomi originali o
  Device Instance ID:
  `UninspectedNestedExecutables`,
  `UninspectedDeeperArchives`,
  `UninspectedProprietaryPackages`.
- `LocalOemArchiveBatchSummary.Combine` somma i contatori
  validati, preserva la causa reale della copertura parziale e non
  etichetta più un EXE proprietario come ZIP/CAB troppo profondo.
  Report legacy con solo flag parziale restano esplicitamente
  parziali, **senza inventare la causa**.
- La schermata import PCSpecialist distingue programmi esterni da
  quelli interni ed espone il motivo del limite anche se i dispositivi
  PnP correlati sono zero. `Summary` include «COPERTURA PARZIALE».
- Test: ZIP solo MSI/7z/RAR/MSIX/ISO, EXE proprietario + MSI,
  due bundle con INF NVIDIA/Killer e omissioni, profondità >2,
  contatori non validi, compatibilità legacy e redazione percorsi.

## Vincoli non modificati
Tutti gli input rimangono opt-in, locali, limitati a 2 livelli,
4 EXE con overlay ZIP su 8 archivi annidati, budget complessivo
1 GiB e gli ulteriori controlli ZIP/CAB/INF preesistenti. Non
vengono eseguiti, estratti o installati MSI/EXE/7z/RAR/ISO.
Non si recupera alcun nuovo update WUA; l'assenza di file INF
leggibili **non prova assenza di driver**. CAT, autenticità OEM,
ranking SetupAPI e compatibilità restano da verificare.
QA su Recoil 16 reale: #120, #136, #91. Firma dell'installer: #283.
