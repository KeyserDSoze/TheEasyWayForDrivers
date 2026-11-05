# PCSpecialist Recoil e copertura driver — sviluppo #116, #117, #118

## Scenario reale

Su un PC **PCSpecialist Recoil 16**, OmegaDrive riconosceva
NVIDIA/Intel/Killer e interrogava Windows Update, ma il catalogo
`SystemOemCatalog` non identificava **PCSpecialist** come produttore
del computer. Venivano quindi suggeriti solo canali dei componenti
anziché il produttore che ha assemblato quella configurazione.

Il portale PCSpecialist ufficiale è:

https://www.pcspecialist.co.uk/download-drivers/

Il sito richiede il **numero dell'ordine del PC**, e restituisce i
pacchetti per quella configurazione, non per tutti i Recoil con
nome simile. Esistono driver Windows 11 pubblicati per le famiglie
Recoil, inclusi NVIDIA, Intel Bluetooth/Wi-Fi, chipset, audio,
network, Thunderbolt, Control Center. Queste sono versioni di
fabbrica/pacchetti di supporto, **non una valutazione che siano più
recenti dei driver già installati**.

**Privacy:** OmegaDrive non richiede, conserva, legge o trasferisce
il numero dell'ordine o seriale al sito. Il pulsante nella UI apre
solo la homepage HTTPS ufficiale in un browser esterno; l'utente
inserisce il numero direttamente sul sito del produttore. Non
vengono effettuate scansioni non documentate del portale,
download in automatico o installazioni.

## Correzioni di questa versione

1. `SystemOemCatalog.Match` supporta `PCSpecialist` e
   `PC Specialist`, con test di normalizzazione e valori simili
   non ammessi.
2. `SystemOemDriverProvider` propone ora il supporto per
   **PCSpecialist** sui componenti interni; schede OEM-specific
   come System/Extension e GPU portatili vengono indirizzate al
   produttore PC prima di seguire driver generici.
3. Nuovo pulsante **Driver PCSpecialist** nella sezione OEM
   della schermata principale, visibile solo se il produttore
   rilevato è PCSpecialist, con testo chiaro su numero ordine,
   privacy e compatibilità.
4. La ricerca di Microsoft Update non registrato
   (`NotRegistered`) non viene più trattata nella UI come un
   fallimento della ricerca dei driver. La fonte rimane
   esplicitamente elencata come «non interrogata» nei dettagli.
5. Nuova lettura dei **suggerimenti di versione solo dal titolo
   Windows Update** quando un titolo contiene una sequenza
   numerica esplicita finale, per esempio:
   `AudioProcessingObject Driver Update (1.0.4.55794)` oppure
   `NVIDIA - Display - 32.0.15.6614`.
   La UI dice «Versione nel titolo WU (da verificare)»; la versione
   è informativa e non influenza confronto driver/ordinamento,
   firma, autorizzazione alla loro installazione o `Aggiorna tutto`.
   Le date, versioni non numeriche e stringhe ambigue non vengono
   interpretate.

## QA automatica

- [x] Riconoscimento PCSpecialist Recoil e alternative ragionevoli,
  più regressioni su vendor non corrispondenti.
- [x] Portale con origine verificata, invariabile e HTTPS.
- [x] Selezione OEM PCSpecialist per driver GPU e componenti
  piattaforma NVIDIA, con per-driver hardware evidence.
- [x] Copertura: `NotRegistered` non produce avviso di ricerca
  incompleta se gli altri pass hanno avuto successo;
  `Failed` sì.
- [x] Estrazione versioni da titolo NVIDIA/Intel/Microsoft e rifiuto
  di date, KB, URL, testo errato, stringhe troppo lunghe.
- [x] Contratto statico UI Windows: pulsante PCSpecialist nascosto
  per impostazione predefinita.
- [ ] CI Windows compilazione/test: verificare su PR.

## Prove da eseguire sul PC

- [ ] Installare la versione, aprire **Analizza il PC**. In
  `Controlla con il produttore` deve comparire **Driver
  PCSpecialist**, senza chiedere un numero d'ordine nell'app.
- [ ] Sul sito ufficiale, individuare solo i pacchetti della
  configurazione acquistata; non incollare numeri d'ordine in
  GitHub o nel report copiabile.
- [ ] Confrontare versioni già installate con quelle dei
  pacchetti NVIDIA, Intel e audio che PCSpecialist propone.
  Non installare un pacchetto soltanto perché esiste.
- [ ] Copiare **report della ricerca** dall'app e controllare
  le sorgenti WUA (compreso `Microsoft Update non registrato`
  o `interrogata`), i nuovi ID e il numero di dispositivi.
- [ ] Verificare su 100%, 125% e 150% DPI che l'interfaccia
  rimanga leggibile.
- [ ] Per il test di installazione eseguire solo su VM/sistema
  con backup, con consenso.

### Limite esplicito

Questo incremento **non promette più driver installabili** rispetto
alla v0.0.56, perché PCSpecialist non espone un'API pubblica
di applicabilità documentata senza numero d'ordine e i suggerimenti
nel titolo WUA non sono nuove versioni da installare.

Il benchmark reale «2 risultati OmegaDrive vs ~8 Driver Easy» resta
aperto in #91. Il passo successivo deve confrontare gli ID e le
versioni delle liste OEM ufficiali e del report WUA senza usare
cataloghi non autorizzati.

Fonti:
- https://www.pcspecialist.co.uk/download-drivers/
- https://www.intel.com/content/www/us/en/support/articles/000023000/software/software-applications.html
