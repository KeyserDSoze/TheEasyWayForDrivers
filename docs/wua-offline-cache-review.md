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

## Flusso

Durante **Analizza il PC → modalità Comprehensive** il servizio:

1. Completa normalmente le pass WUA **online Type=Driver**
   e la ricerca opzionale Type=Software.
2. Esegue una **query separata e read-only** nel WUA datastore
   esistente: `session.CreateUpdateSearcher()`,
   `searcher.Online = false`,
   `IncludePotentiallySupersededUpdates = true`,
   `Search("IsInstalled=0 and Type='Driver'")`.
   Non imposta `ServerSelection` o `ServiceID`, non
   registra Microsoft Update e non modifica WSUS o policy.
3. Accetta solamente `ISearchResult` con risultato completo,
   senza avvisi. Scarta interamente un pass con errore
   strutturale, GUID illeggibile o enumerazione incompleta.
   I getter `Title` opzionali guasti scartano solo il titolo.
4. Legge fino a **4.000 record COM** e conserva massimo
   **32 titoli distinti** ben formati: GUID per dedup interna,
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
