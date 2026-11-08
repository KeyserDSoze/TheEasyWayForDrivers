# INF stampanti Windows: bus PnP riconosciuti in Discovery+ (#252)

## Limite risolto

OmegaDrive usa una lista rigorosa di enumeratori Windows PnP per leggere
gli Hardware ID dichiarati nei Models di un INF locale. Prima di questo
intervento mancavano diversi **bus di stampa** documentati da Microsoft;
un pacchetto con un ID `USBPRINT\\Hewlett-PackardHP_Co3115` poteva
quindi apparire erroneamente senza corrispondenza hardware, anche quando
Windows aveva rilevato il medesimo identificativo.

La specifica Microsoft *Plug and Play IDs for Printer INF Files*
documenta gli identificatori per USB, parallela, Dot4, Bluetooth, WSD
e IEEE 1394:

https://learn.microsoft.com/en-us/windows-hardware/drivers/print/plug-and-play-ids-for-printer-inf-files

## Nuovo riconoscimento read-only

Il parser `InfModelEvidence` accetta nei file INF le seguenti famiglie
**solo se bus-qualified**:

- `USBPRINT\\...` — USB printer
- `LPTENUM\\...` — porta parallela
- `DOT4PRT\\...` — Dot4
- `BTHPRINT\\...` — stampante Bluetooth
- `WSDPRINT\\...` — Windows Services for Devices
- `1394\\...` — IEEE 1394

`HardwareIdentityParser.Describe` li presenta come periferiche di
stampa, senza dedurre produttori o driver disponibili. Il matching
richiede **uguaglianza PnP esatta** con gli ID Windows già letti per
quel dispositivo: nessuna ricerca per parole nel nome e nessuna
promozione del modello generico a idoneità per installazione.

L'estensione è limitata ai bus elencati; non accetta separatori di
istanza aggiuntivi, percorsi, caratteri fuori dall'ASCII di sicurezza,
script, Unicode di controllo o bus sconosciuti. La prima colonna ID
del Models INF continua a essere distinta dalle colonne **compatible**:
un alias di secondo livello rimane una prova più debole e non può
generare da solo un suggerimento di versione aggiornata.

## Superfici interessate e limiti

ZIP OEM, cartelle estratte e CAB convertiti al parser ZIP riutilizzano
la stessa logica. Il confronto è **in sola lettura**, senza estrarre
eseguibili, installare o modificare Windows Driver Store. I limiti
96 INF / 24 MiB per ZIP e 384 INF / 96 MiB per cartella restano
invariati.

Un ID stampante corrispondente a un INF non prova la firma CAT,
il ranking SetupAPI, l'applicabilità definitiva, che esista una
versione più recente o un pacchetto scaricabile. Non aggiunge
aggiornamenti alla lista WUA o al pulsante **Aggiorna tutto**.

Microsoft indica la piattaforma moderna di stampa e il driver IPP
di Windows come soluzione preferita per i nuovi sviluppi di stampa:
https://learn.microsoft.com/en-us/windows-hardware/drivers/print/plug-and-play-ids-for-printer-inf-files

## QA

I test includono sei enumeratori ufficiali, USB/Bluetooth/WSD in un
archivio ZIP, USB/Dot4 da cartella estratta, priorità fra Hardware
ID primario e Compatible ID secondario, identificativi quasi uguali,
istanze PnP aggiuntive, path traversal, caratteri di controllo, bus
fittizi, e report che non dichiarano installabilità.

Il benchmark NVIDIA/Killer/Realtek sul Recoil 16 (**2 vs 8**)
rimane separato e aperto nelle issue #91 e #136.
