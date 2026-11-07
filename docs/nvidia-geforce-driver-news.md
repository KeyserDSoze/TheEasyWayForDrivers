# OmegaDrive — annunci driver NVIDIA GeForce (issue #210)

## Perché

La v0.0.100 correla meglio i GUID restituiti da WUA ai dispositivi,
ma non interroga un feed ufficiale NVIDIA di driver applicabili.
NVIDIA App è il companion **ufficiale** per la verifica degli
aggiornamenti Game Ready / Studio; NVIDIA non documenta una API
anonima stabile che restituisca quali driver sono applicabili
alla GPU NVIDIA di uno specifico portatile.

Un sito diverso, Microsoft Update Catalog, possiede ricerche HTML
pubbliche. Tuttavia la pagina Search.aspx/WebForms non è un'API
documentata: i risultati dipendono da JavaScript/ASP.NET e da
cambiamenti del layout che possono far mancare il risultato anche
per un ID noto. Non la usiamo qui come fonte automatica
di nuovi aggiornamenti installabili.

## Nuova funzione: notizie GeForce pubblicate da NVIDIA

Dopo **Analizza il PC**, quando è riconosciuta una GPU NVIDIA
(`Display` + rilevamento locale della firma hardware), in
**Dettagli avanzati** compare il pulsante **Novità driver NVIDIA**.

Al clic:

1. Chiede il consenso esplicito all'accesso al feed pubblico
   `https://www.nvidia.com/content/rss/geforce/news/index.php`.
   NVIDIA limita l'uso del proprio RSS a finalità **non commerciali**:
   la funzione è opt-in e non si attiva senza consenso.
2. Effettua **una GET** alla URL fissa, senza query string,
   Hardware ID, Device Instance ID, seriali, numero ordine,
   nome PC, versione Windows o versione del driver.
3. Impone **12 secondi** di timeout, **512 KiB** di XML,
   nessun redirect, DTD/XXE bloccati; analizza al massimo
   **80** articoli RSS.
4. Visualizza al massimo **quattro** notizie negli ultimi
   **90 giorni**, con titoli contenenti Game Ready Driver
   o Studio Driver. Esclude notizie esclusive RTX Spark non
   menzionanti GeForce, articoli non sui driver e link
   non su `www.nvidia.com/en-us/geforce/news/`.
   Rimuove parametri e frammenti di tracciamento dai link.
5. Le notizie sono visibili in **Altri dispositivi e fonti**;
   possono essere aperte sul sito ufficiale via HTTPS.
   Non vengono presentate nella lista dei driver ordinari,
   candidati WUA o in **Aggiorna tutto**.

## Cosa significa (e cosa non significa)

**Notizia di driver pubblicato** non significa «il tuo
PC richiede questo aggiornamento». Il feed non è un catalogo
di verifica individuale; non documenta l'effettiva compatibilità
della GPU, l'OEM laptop, il SUBSYS, il tipo DCH, il ranking Windows,
i file CAT/INF e le dipendenze. La presenza di un comunicato
non viene confrontata con l'installazione locale e non crea
`DriverUpdateInfo`, suggerimenti per l'installazione né download.

**Feed non raggiungibile / senza notizie** non significa che non
esistano driver più recenti. NVIDIA App, la ricerca manuale NVIDIA
e i driver qualificati OEM (PCSpecialist) restano i riferimenti.
Il numero d'ordine PCSpecialist deve restare sul sito ufficiale.

Nessuna installazione automatica, EXE, MSI, firmware, modifica WUA,
accesso a endpoint NVIDIA non documentati o polling in background.

## Test

Parser con feed RSS sintetico: articoli Game Ready/Studio vs
pubblicità/articoli non driver/RTX Spark; età data; URL HTTPS e
dominio/percorso allowlisted; reindirizzamenti vietati lato client;
DTD/XXE, XML malformato, limiti, controllo Unicode e titoli
inattesi, deduplica URL, rimozione tracciamenti; tutti i risultati
`IsInstallable=false`. La CI Windows compila Core e WPF e
testa UX e contratto IPC. **Per la disponibilità/struttura RSS
reale serve una prova runtime con connessione sul PC**.

Le issues #22 (eventuale integrazione catalogo NVIDIA supportato),
#91 (gap 2-vs-8) e #136 (QA Recoil 16) rimangono aperte.

Fonti: [NVIDIA driver manual search](https://www.nvidia.com/en-us/geforce/drivers/),
[NVIDIA App FAQ](https://nvidia.custhelp.com/app/answers/detail/a_id/5521/),
[NVIDIA RSS e condizioni](https://www.nvidia.com/en-us/about-nvidia/rss/).
