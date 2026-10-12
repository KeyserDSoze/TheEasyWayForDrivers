# Discovery+ — risultati unificati (prima fase)

La scheda **Discovery+** mostra metadati della sessione corrente provenienti da fonti differenti, senza appiattire il significato delle voci.

| Fonte | Tipologia | Quando viene aggiornata | Installabile da questa vista? |
| --- | --- | --- | --- |
| Windows Update | Candidati driver WUA | Ogni ricerca WUA; invalidata da nuova scansione inventario hardware | No (installazione WUA disponibile soltanto nella scheda dedicata con conferma) |
| Dell DriverPackCatalog | Pack di deployment per modello OEM dichiarato | Consultazione Dell esplicita | No |
| HP CMSL | SoftPaq di categoria Driver restituiti dal modulo ufficiale | Consultazione HP esplicita | No |
| Lenovo Client Scripting | Risultati non ancora normalizzati | Consultazione nella scheda Provider OEM | No; non elencati nella vista unificata finché lo schema non sarà validato |

Le voci mostrate sono **indizi/catalog metadata**, non attestazioni della compatibilità con singoli dispositivi Windows. Il match preciso con Hardware ID o Compatible ID nella vista dispositivo resta un processo distinto. Date/versioni non vengono confrontate automaticamente attraverso fonti che adottano convenzioni differenti.

## Privacy e limiti
- I risultati vengono mantenuti **solo in memoria** nella sessione desktop, senza salvarli su disco e senza inviare seriali a servizi terzi attraverso questa vista.
- La consultazione dei cataloghi OEM viene avviata solo dall'utente nella scheda Provider OEM. Non esiste un pulsante per installare pacchetti Dell/HP/Lenovo nella vista Discovery+.
- I risultati Dell possono includere un pacchetto con più driver e quindi **non** corrispondono a un singolo aggiornamento Windows.
- I dati HP sono metadati di catalogo SoftPaq, non l'esito di una scansione Hardware ID locale. BIOS e firmware sono esclusi dal parser.
- I risultati Lenovo richiedono un adapter rigoroso basato su output verificato del cmdlet; non indovinare il mapping JSON.
- Prima di una vera integrazione di download servono verifica di compatibilità modello/OS/architettura, hash, firma, gestione rollback e QA #5.

Roadmap: Issue #34, #9, #12, #14, #15.
