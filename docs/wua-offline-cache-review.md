# Scansione locale offline dell'archivio WUA — issue #246

## Perché è stata aggiunta

OmegaDrive esegue una ricerca online dei driver Windows Update
(`Type='Driver'`) e una ricerca separata dei componenti
`Type='Software'`. In caso di risultati scarsi — ad esempio,
**2 GUID Windows** contro **8 candidati di Driver Easy** nel
Recoil 16 — non possiamo dedurre che gli altri driver siano
assenti dal catalogo OEM, e non possiamo ricavare gli otto
candidati interrogando soltanto WUA.

Microsoft documenta che `IUpdateSearcher.Online=false` è una
**scansione offline dell'archivio locale** di Windows Update,
utile soltanto se in passato è stata effettuata una scansione
online. I dati possono essere **obsoleti, superseduti o già
installati**: non equivalgono ad aggiornamenti attualmente
disponibili, verificati o applicabili.
Fonte Microsoft:
https://learn.microsoft.com/en-us/windows/deployment/update/how-windows-update-works

## Correzione #248 — includere anche i driver installati nella cache

La v0.0.118 eseguiva unicamente `IsInstalled=0 and Type='Driver'`.
Il report storico era quindi incompleto: un driver già installato
da Windows non compariva neppure tra i titoli locali.

Ora due pass WUA offline **indipendenti** interrogano:

- `IsInstalled=0 and Type='Driver'` — record cache segnati non installati;
- `IsInstalled=1 and Type='Driver'` — record cache segnati installati.

Ogni pass crea un nuovo `IUpdateSearcher`, usa `Online=false` e
`IncludePotentiallySupersededUpdates=true`, senza modificare
`ServerSelection` / `ServiceID`, servizi o policy Windows.
La seconda scansione non è un ulteriore controllo online.
Se una pass fallisce, gli esiti completi dell'altra restano disponibili
con avviso **copertura offline parziale**.

I record non installati e installati vengono combinati con una
**preview bilanciata**: uno di ciascuno per turno fino a **32
GUID storici totali**, indipendentemente dall'ordine delle
rilevazioni. Ogni pass legge al massimo **4.000 record**, il
GUID è deduplicato case-insensitive e i GUID già offerti online
sono esclusi. Se uno stesso GUID viene restituito come sia
installato sia non installato, la preview mostra **stato
sconosciuto**, non una certezza falsa.

La UI e «Copia copertura» distinguono record segnati installati,
non installati e non classificabili. Sono soltanto stati registrati
nell'archivio Windows locale: **nessuna prova di disponibilità
attuale, compatibilità OEM, nuova versione o installabilità**.
`DriverUpdateInfo`, download, installazione e `Aggiorna tutto`
rimangono invariati.

Fonte dei filtri WUA:
https://learn.microsoft.com/en-us/windows/win32/api/wuapi/nf-wuapi-iupdatesearcher-search

## Flusso

Durante **Analizza il PC → modalità Comprehensive** il servizio:

1. Completa normalmente le pass WUA **online Type=Driver**
   e la ricerca opzionale Type=Software.
2. Esegue **due query indipendenti, read-only** nel WUA
   datastore esistente con `session.CreateUpdateSearcher()`,
   `searcher.Online = false` e
   `IncludePotentiallySupersededUpdates = true`: per
   `IsInstalled=0 and Type='Driver'` e
   `IsInstalled=1 and Type='Driver'`.
   Non imposta `ServerSelection` o `ServiceID`, non
   registra Microsoft Update e non modifica WSUS o policy.
3. Accetta solamente `ISearchResult` con risultato completo,
   senza avvisi. Scarta interamente un pass con errore
   strutturale, GUID illeggibile o enumerazione incompleta.
   I getter `Title` opzionali guasti scartano solo il titolo.
4. Legge fino a **4.000 record COM per ricerca** e conserva massimo
   **32 titoli distinti complessivi**, bilanciati fra i due stati ben formati: GUID per dedup interna,
   titolo pubblico e **nessun altro dato**.
   Esclude GUID già confermati dalla ricerca online.
5. Trasmette i dati con campi **opzionali** aggiunti a
   `DriverSearchReport`: `CachedDriverHints`,
   `CachedDriverSources`, `CachedDriverHintsOmitted`.
   I vecchi client JSON/IPC restano compatibili.
6. La Home visualizza solo un avviso quando la cache
   contiene titoli storici da verificare. Nei
   **Dettagli delle fonti → Archivio Windows locale** è
   presente un elenco separato e chiaramente marcato
   **storico / non installabile**, mai nella lista principale
   degli aggiornamenti WUA disponibili.

## Confini di sicurezza

**Non è una nuova fonte di aggiornamenti verificati.** I dati
offline NON diventano `DriverUpdateInfo`, non vengono associati
ai device come aggiornamenti confermati, non aumentano
`Update All`, `QuickUpdatePolicy` o le notifiche automatiche.
Nessun download, staging, installazione driver, lancio EXE,
scraping NVIDIA/Intel/Realtek o accesso a un portale OEM.

Anche se un titolo storico indica una versione maggiore,
è possibile che Windows non la offra più o che il sistema
non sia compatibile: la presenza nella cache non prova un
aggiornamento necessario, sicuro o più recente.

La ricerca cached è **opzionale e indipendente**: se fallisce,
lo stato WUA Type=Driver online e le azioni ordinarie non
vengono disabilitati. Se la cache è vuota, non se ne deduce
l'assenza di driver esterni.

Il report copiabile mostra solo **conteggi aggregati**
e se la fonte offline è verificata; non stampa titoli,
GUID, identificativi PnP, percorsi, seriali o dati privati.

## Test

- Validazione query offline: `Online=false`, nessuna modifica
  di `ServerSelection` / `ServiceID` / WSUS.
- Lettura WUA completa o rifiuto intero pass su errore COM,
  risultato parziale o GUID invalido; cancellazione propagata.
- Titoli NVIDIA/Killer/Realtek da fixture sintetiche,
  `Title` COM opzionale non leggibile, sanitizzazione,
  case-insensitive GUID dedup, 32 mostrati e omitted.
- Nessun conflitto con GUID online installabili, Software,
  report di copertura privato, retrocompatibilità IPC.

La disponibilità reale dei titoli della cache dipende dallo
stato di Windows e va verificata sul PC Recoil 16. Il benchmark
Driver Easy 2 vs 8 rimane aperto (#91, #136).

## Provenienza delle righe IPC nello storico offline (#416, 12 novembre 2026)

La UI e la copia anonima **non** consumano più direttamente i titoli `CachedDriverHints` di un report IPC legacy. `WuaCachedDriverHintCollector.ConfirmedForReport` richiede i due nomi esatti delle ricerche offline `IsInstalled=0/1`, rifiuta pass duplicati/contraddittori o con contatori `Completed` impossibili e ricontrolla i GUID, titoli e limiti (32 righe). Un titolo marcato *installato* richiede il pass installato completo, uno *non installato* richiede quello non installato; lo stato *sconosciuto* è accettato solo se **entrambi** sono stati verificati. Una fonte completata non autentica i titoli dell'altra fonte non riuscita. I vecchi payload con sole etichette storiche generiche non certificano un pass.

Lo stato offline parziale e la conta dei titoli nel report copiabile usano la stessa evidenza filtrata. Il numero `CachedDriverHintsOmitted`, non attribuito a un singolo pass nel protocollo, viene presentato come verificato soltanto quando entrambe le fonti sono completate. La cache rimane **storico locale, non disponibilità online**; una scansione online valida e `Aggiorna tutto` non cambiano stato in caso di errore della cache. Test sintetici su due fonti indipendenti, errore di una, falsi `Completed`, GUID duplicati, titoli con percorsi, snapshot malformati e stati installato/non installato/ambiguo. QA Windows reale #136 e firma #283 restano aperte.
