# Dell Command Update XML: alias e campi duplicati (#510)

## Problema
Il parser locale del report `dcu-cli.exe /scan -updateType=driver`
leggeva la prima occorrenza non vuota dei campi diretti di
`<Update>`: attributi e nodi figli, con alias per `Type`,
`Id`, `Name`, `Version`, `Severity`. Due dichiarazioni
contraddittorie (ad esempio attributo `type="Driver"` e figlio
`<UpdateType>Firmware</UpdateType>`) potevano diventare
evidenza di un driver Dell senza riconciliazione.

## Regole fail-closed
- `RawValue` enumera **tutti** gli attributi e figli diretti
  appartenenti al gruppo di alias richiesto.
- Una seconda dichiarazione, anche con casing diverso, namespace
  diverso, valore identico oppure vuoto, invalida **l'intero report**;
  in questo modo l'app non presenta risultati parziali come
  "nessun aggiornamento" o "driver necessario".
- Restano validi gli attributi o figli unici, inclusi alias e
  documenti namespaced. Un campo con XML annidato resta invalido.
- Limiti 1 MiB XML, niente DTD, 150 driver unici e privacy-safe error
  messages restano invariati.

## Test
`DellDuplicateXmlMetadataBoundaryTests` verifica collisioni
Type/UpdateType, Id/ComponentID/UpdateID/PackageID, Name/Title,
Version/ReleaseVersion, Severity/UpdateSeverity; report con driver
precedente valido e seconda entry ambigua; XML namespaced; alias
singoli preservati e campi opzionali vuoti.

Il connettore resta una diagnostica Dell read-only: non vengono
eseguiti comandi di update. La validazione DCU reale #104,
Windows end-to-end #85/#5 e firma #283 restano aperte.
