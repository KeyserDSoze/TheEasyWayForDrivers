# Cataloghi OEM ufficiali — Discovery+ (v0.0.21)

Questo modulo **non** usa scraping privato, pacchetti da siti terzi o installazione automatica. Il catalogo WUA resta la fonte dei driver installabili dal servizio; i cataloghi OEM sono **metadati informativi** finché compatibilità e firma dei payload non sono verificate separatamente.

## Dell — supportato in sola lettura (primo connettore)
- Documentazione Dell: https://www.dell.com/support/kbdoc/en-td/000122176/driver-pack-catalog
- Catalogo pubblicato: https://downloads.dell.com/catalog/DriverPackCatalog.cab
- Formato: CAB contenente `DriverPackCatalog.xml`. Catalogo di **Driver Pack di deployment** (Latitude, OptiPlex, Precision, alcune altre famiglie), **non** di tutti i singoli driver Dell.
- OmegaDrive scarica il CAB su richiesta esplicita, limita dimensione e tempo, estrae soltanto `DriverPackCatalog.xml` con `expand.exe` di Windows, blocca DTD e riferimenti XML esterni. Nessun EXE/INF del catalogo viene eseguito o installato.
- Filtraggio conservativo per nome esatto del modello rilevato da WMI. I risultati mostrano informazioni sul sistema operativo dichiarato, non la compatibilità accertata. In particolare l'ID BIOS/Sistema è preferibile al nome e va verificato prima di qualsiasi installazione.
- Dell Command Update usa oggi anche CatalogIndexPC e cataloghi per piattaforma: https://www.dell.com/support/kbdoc/en-in/000453493/dell-client-commercial-pcs-update-catalog-cadences — integrazione successiva, schema distinto.

## Lenovo — API ufficiali documentate, adattatore da sviluppare
- Modulo PowerShell **Lenovo.Client.Scripting**, documentazione: https://docs.lenovocdrt.com/guides/lcsm/lcsm_top/
- `Find-LnvUpdate -MachineType <codice-quattro-caratteri> -WindowsVersion 11 -PackageType 2 -ListAll` restituisce gli aggiornamenti driver del catalogo Lenovo, a seconda del modello supportato.
- Richiede modulo installato dall'utente e MachineType corretto; non trattare un PowerShell proprietario come endpoint REST anonimo.
- Lenovo System Update, Update Retriever e Thin Installer sono strumenti ufficiali alternativi: https://docs.lenovocdrt.com/guides/sus/

## HP — API ufficiali documentate, adattatore da sviluppare
- HP Client Management Script Library (CMSL), cmdlet `Get-SoftpaqList` con `-Platform`, `-Os` e `-OsVer`: https://test.hpopenprint.com/hp-client-management/doc/get-softpaqlist
- Richiede identificazione corretta della piattaforma, installazione del modulo CMSL ufficiale e verifiche della versione Windows; non implica copertura di qualunque dispositivo HP.

## Requisiti futuri
1. Identificare modello *e* ID piattaforma del PC senza trasmettere seriali o identificativi utente.
2. Adapter con interfaccia comune per enumerazione read-only e provenienza documentata.
3. Escludere BIOS/firmware dal percorso di installazione driver generico.
4. Verificare compatibilità Windows/architettura, firme Authenticode e hash pubblicati prima di qualunque funzione di download.
5. Esporre fonte OEM, data del catalogo, stato disponibile/non verificato e limiti di copertura; aggiungere test di integrazione e QA Windows 11.

## Limiti del primo connettore
Non importa aggiornamenti OEM nella lista WUA, non rileva singoli driver e non garantisce compatibilità. Dell può cambiare formato, CDN e disponibilità del CAB: in tal caso l'app visualizza l'errore senza alcuna installazione.
