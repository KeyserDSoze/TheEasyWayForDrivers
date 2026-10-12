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


## Matrice completa delle integrazioni (review 12 ottobre 2026)

Le classificazioni qui sotto rappresentano **capacità documentate**, non accesso garantito ad API utilizzabili senza autenticazione. Non introdurre scraping, credenziali OEM o download non verificati.

| OEM | Fonte ufficiale | Accesso consentito / realistico | Stato | Issue |
| --- | --- | --- | --- | --- |
| Dell | [Dell Command Deploy Driver Pack](https://www.dell.com/support/kbdoc/en-uk/000122176/driver-pack-catalog) | CAB/XML pubblico, metadata read-only dei pacchetti di deployment | Parser/UI in PR #13; runtime da validare | #12 |
| Lenovo | [Lenovo Client Scripting](https://docs.lenovocdrt.com/guides/lcsm/lcsm_top/) | Cmdlet documentato `Find-LnvUpdate` con MachineType 4 caratteri, WindowsVersion 11, PackageType 2 | Adapter PowerShell da realizzare | #14 |
| HP | [HP CMSL](https://developers.hp.com/hp-client-management/doc/get-softpaqlist) | Cmdlet `Get-SoftpaqList` per piattaforma e OS; richiede modulo ufficiale | Adapter PowerShell da realizzare | #15 |
| ASUS | [Supporto MyASUS](https://www.asus.com/support/faq/1050467/) | Tool/portale ufficiale, System Control Interface; nessuna API catalogo pubblica verificata | Handoff, ricognizione | #16 |
| Acer | [Driver e manuali](https://www.acer.com/it-it/support/drivers-and-manuals) | Ricerca tramite modello, P/N o SNID; evitare raccolta/invio automatico di numeri di serie | Handoff, ricognizione | #17 |
| MSI | [MSI Center Live Update](https://it.msi.com/support/technical_details/DT_Driver_Update) | Companion ufficiale con scansione manuale; nessuna API catalogo pubblica verificata | Handoff, ricognizione | #18 |
| GIGABYTE | [Supporto GIGABYTE](https://www.gigabyte.com/Support) | Sito/supporto e Control Center; nessuna API pubblica verificata | Handoff, ricognizione | #19 |
| Microsoft Surface | [Surface MSI e Windows Update](https://learn.microsoft.com/en-us/surface/manage-surface-driver-and-firmware-updates) | MSI ufficiali per modello/OS, Windows Update; firmware da trattare separatamente | Handoff, possibile indicizzazione documentata | #20 |
| Intel | [Intel DSA](https://www.intel.com/content/www/us/en/support/detect.html) | Utility ufficiale con supporto per hardware Intel; nessuna API anonima garantita | Handoff e detection già presenti | #21 |
| NVIDIA | [NVIDIA App](https://www.nvidia.com/en-us/software/nvidia-app/) | Companion ufficiale per driver GPU; nessuna API anonima garantita | Handoff e detection già presenti | #22 |
| AMD | [AMD Auto-Detect](https://www.amd.com/en/resources/support-articles/faqs/GPU-131.html) | Utility ufficiale Radeon/chipset; supporto dipendente da hardware, Windows e OEM | Handoff e detection già presenti | #23 |
| Samsung | [Samsung Support](https://www.samsung.com/support/) | Supporto modello e Samsung Update dove disponibile, nessuna API pubblica verificata | Discovery da pianificare | #24 |
| Framework | [Framework Support](https://frame.work/support) | Driver bundle pubblicati per modello e generazione, nessuna API pubblica verificata | Discovery da pianificare | #25 |
| Razer | [Razer Support](https://mysupport.razer.com/) | Supporto driver/software per dispositivo, nessuna API pubblica verificata | Discovery da pianificare | #26 |
| Logitech | [Logitech Support](https://support.logi.com/) | Software Logi Options+/G HUB, distinto dai driver PnP di base | Discovery da pianificare | #27 |

### Contratto comune proposto

Ogni sorgente espone *provenienza*, *stato di accesso* (`PublishedCatalog`, `DocumentedLocalTool`, `OfficialSupportOnly`), *copertura*, *richieste per modello/OS*, *metodo autorizzato* e *evidenza di compatibilità*. Nessun pacchetto OEM deve essere marcato installabile senza un'ulteriore validazione. Le integrazioni locali devono chiedere consenso prima di invocare tool già installati; non installarli silenziosamente.

### Roadmap implementativa

1. **#12 Dell**: validazione del formato CAB/XML ufficiale, matching esatto, risultati deployment (prima release, PR #13).
2. **#14 Lenovo**: adapter read-only su `Lenovo.Client.Scripting` installato, con risposta strutturata e fallimento esplicito se MachineType non è verificabile.
3. **#15 HP**: adapter read-only `HP CMSL`; stessa disciplina di versione OS, Platform ID e gestione modulo assente.
4. **#20 Surface**, poi **#16–#19**, **#21–#27**: supporto documentato e integrazioni automatiche **solo se emergono interfacce supportate**.
5. Test unitari, UI unificata e QA hardware #5 per ciascun vendor.

Ogni integrazione dev'essere sviluppata in branch/PR separati, con report della CI e limiti documentati. Una fonte di supporto non equivale a un catalogo aggiornamenti.
