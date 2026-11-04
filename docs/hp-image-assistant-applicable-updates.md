# HP Image Assistant — raccomandazioni driver sul PC corrente (#108)

## Problema e differenza da CMSL

`Get-HPSoftpaqList` (CMSL, #106) fornisce metadati ufficiali per piattaforma
e categoria; **non è una prova** che ogni SoftPaq sia necessario al PC.

HP Image Assistant (HPIA), tramite un report di `/Operation:Analyze` sul
computer corrente, distingue raccomandazioni **Drivers** da BIOS, Firmware
e Software e può riportare le versioni target/reference. Questa
integrazione è **solo consultazione**: non autorizza installazioni.

Fonti:
- HP Image Assistant official product: https://ftp.ext.hp.com/pub/caps-softpaq/cmit/HPIA.html
- HP official user guide 2025:
  https://ftp.ext.hp.com/pub/caps-softpaq/cmit/whitepapers/HPIAUserGuide.pdf
- HP support (HPIA extraction location and admin requirements):
  https://support.hp.com/emea_africa-en/document/ish_7636709-7636753-16
- Example of authentic HPIA report XML shape (HP support community):
  https://h30434.www3.hp.com/t5/Notebook-Software-and-How-To-Questions/Bug-Report-Software-Recommended-and-Firmware-OutOfDate-wrong/td-p/7989118

## Esperienza utente

1. Premere **Analizza il PC**. Su PC HP (incluso `HP Inc.`) appare
   **Controlla i driver necessari con HP**.
2. Il click richiede consenso e informa su rete, amministratore, firma
   HP obbligatoria e trattamento temporaneo del report (che può includere
   identificativi del PC). HPIA deve essere **già installato/estratto**.
3. OmegaDrive cerca HPIA nei percorsi noti Program Files/HP e
   `%SystemDrive%\SWSetup\HPIA` o `SWSetup\spNNNNN` (profondità 1 e
   numero limitato), mai esegue da PATH o dalla cartella Downloads.
   Verifica `Get-AuthenticodeSignature` con stato **Valid** e publisher
   HP prima di lanciare il binario. Senza verifica: blocco.
4. Viene chiamato esclusivamente:
   `HPImageAssistant.exe /Operation:Analyze /Category:Drivers /Selection:All /Action:List /Silent /ReportFilePath:<cartella temporanea>\Recommendations`
5. OmegaDrive legge **solo** il documento XML `Recommendations.xml`
   che HPIA produce nella cartella temporanea, con DTD/entità esterne
   disabilitate, limite 4 MiB e massimo 150 righe.
6. Dal percorso XML
   `HPIA/Recommendations/Drivers/Recommendation/Solution/Softpaq`
   il parser prende identificativo SP, nome, versione locale e versione
   di riferimento (quando disponibili); ignorate le sezioni
   `BIOS`, `Firmware` e `Software`.
7. La UI presenta i risultati nella sezione produttore con lo stato
   **Consigliato da HP Image Assistant**. Le righe non sono mai passate
   a `DriverUpdates`, al pulsante `Aggiorna tutto`, a WUA o al servizio
   di installazione.
8. Una nuova scansione elimina le precedenti righe HPIA. I pacchetti
   CMSL restano visibili come metadati di catalogo, distinti dalle
   raccomandazioni. Errori/firma non verificata/modulo assente/report
   sconosciuto sono **non verificati**, mai «zero driver».

## Limiti di versione e ambiente

- Sono coperti i report `HPIA` con struttura `Recommendations/Drivers`.
  HPIA può cambiare lo schema; formati diversi richiedono raccolta
  di report anonimizzati e nuova validazione, non interpretazione
  indiscriminata dell'XML.
- Un exit code 0 richiede comunque un report valido, mentre i codici
  256 («no recommendations») e 257 («no recommendations selected»)
  sono successi senza raccomandazioni secondo il manuale HP.
- Rete e amministratore possono essere necessari; il processo può
  impiegare diversi minuti. Timeout di cinque minuti più validazione
  della firma; stop su cancellazione. Pulizia in finally.
- I documenti XML HP possono contenere seriale, nome computer e
  configurazione: OmegaDrive estrae soltanto le raccomandazioni e non
  registra né esporta il contenuto completo.
- Le versioni del pacchetto SoftPaq e quelle PnP di Windows non sono
  sempre direttamente confrontabili. La validità dell'indicazione
  rimane responsabilità del motore HP.

## QA ancora aperta

- [ ] Eseguire su HP Elite/Pro/Workstation supportato, HPIA 5.x
  firmato: valutare firma HP effettiva e percorsi di estrazione.
- [ ] Confrontare risultati visualizzati con il report nativo XML e con
  l'interfaccia HPIA, incluse categorie miste e XML senza driver.
- [ ] Verificare offline, HPIA assente, utente non amministratore,
  mancanza proxy, codici 256/257/4096/4098/8192.
- [ ] Assicurare che il report temporaneo sia eliminato, anche dopo
  errori, e nessun elemento HP compaia in **Aggiorna tutto**.
- [ ] Regressione su PC non HP: nessun pulsante HPIA visibile.

Riferimenti sviluppo: #15 #91 #97 #100 #106 #108.
