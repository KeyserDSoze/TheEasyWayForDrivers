# PCSpecialist: integrità dei conteggi PnP in riconciliazione (#523)

`PcSpecialistPackageReconciliation.Compare` confronta i nomi esatti
dei pacchetti contenuti nell'HTML salvato dall'utente con i risultati
dell'analisi di ZIP/CAB/PE-ZIP selezionati localmente. Il confronto
percorre al massimo 2048 dispositivi Windows, ma i record
`LocalSource` potevano dichiarare miliardi di match senza che
la UI segnalasse l'incoerenza, creando un report fuorviante.

## Correzione

- Le quote `PnpMatches` e `ReviewOnlyMatches` vengono validate
  prima della riconciliazione: somma non negativa e <= 2048,
  usando aritmetica `long` per prevenire overflow.
- I record non esaminati non possono contenere corrispondenze.
- `LocalSource.FromArchive` rifiuta report con `DevicesExamined`
  fuori 0..2048, finding più numerosi dei device esaminati,
  conteggi omitted impossibili e forza HardwareId non definita
  (solo 0, 1, 2).
- Le due liste di ordinali completi devono comparire insieme;
  gli ordinali oltre la preview 80 restano supportati.
- Gli errori sono messaggi generici che non includono ordine,
  nome del PC, INF path o identificativi PnP.

## Test

`PcSpecialistReconciliationCountIntegrityTests` copre input negativi
e `int.MaxValue`, il limite 2048/2049, fonti non esaminate,
forza PnP sconosciuta, coppie di ordinali incomplete, report con
più findings dei dispositivi, il caso reale sintetico 120 device/
60 corrispondenze PnP e la non installabilità del report.

Non si autentica HTML, versione o firma CAT; i dati rimangono
read-only e non offrono alcun nuovo driver all'azione Update All.
Il collaudo con ZIP del proprio ordine su Recoil 16 resta in #120
e #136; #128 conserva i controlli firma CAT e #283 il signing app.
