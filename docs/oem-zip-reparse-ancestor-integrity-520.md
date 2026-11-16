# OEM ZIP: path file/cartella e reparse Windows rifiutati (#520)

## Confine condiviso
`OemArchiveInspector.CreateValidatedZipEntryIndex` viene usato
nell'importazione read-only di archivi ZIP OEM, inclusi alcuni
PE-ZIP ed eventuali archivi annidati. Indicizza le voci usando
separatori unificati e nomi senza distinzione di maiuscole.

## Nuove garanzie
- Un attributo esterno ZIP con **reparse point DOS Windows**
  (`FileAttributes.ReparsePoint`, 0x400) viene rifiutato, così come
  il tipo simbolico Unix 0xA000 già bloccato. Vale per qualunque
  voce, anche una directory, un .cat, un .sys o un file apparentemente
  irrilevante. L'analisi non segue collegamenti.
- Dopo aver costruito l'indice di max 2048 voci, tutti i
  percorsi sono confrontati con i propri antenati. Un elemento
  `drivers/chipset` **file** non può essere anche la directory di
  `drivers/chipset/nvidia.inf`, neanche con slash, casing e ordine
  delle entry differenti.
- Directory esplicite e implicite lecite, contenuti non firmati
  in sola lettura, limiti ZIP e quote di decompressione rimangono
  invariati. Non viene affermata alcuna verifica crittografica o
  installabilità per i file osservati.

## Regressioni
`OemZipReparseAndAncestorIntegrityTests` usa solo ZIP sintetici:
DOS reparse su INF/CAT/SYS/text/cartella, symlink Unix, file padre
contraddittorio in due ordini, slash Windows/Unix e casing,
archivio valido con directory nidificate e nessuna perdita
delle categorie INF/CAT/SYS. Nessun path o identificatore
privato viene copiato nei messaggi di errore.

QA Windows e provenienza/firme CAT reali rimangono nelle issue
#122, #124, #128 e #136.
