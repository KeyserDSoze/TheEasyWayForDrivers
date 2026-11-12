# PCSpecialist: pacchetti HTML oltre 96 riconciliati con la cartella locale (#390)

## Difetto riproducibile

Il parser dell'HTML locale PCSpecialist valida fino a 2.048 collegamenti HTTPS ma prima della correzione restituiva solo le prime 96 voci bilanciate per categoria. La UI passava quelle 96 alla scansione ZIP/CAB/PE-ZIP e agli archivi proprietari: un nome file locale con corrispondenza esatta nel catalogo HTML, ma fuori dalla preview, non poteva mai ricevere uno stato «INF dichiarato» o «formato non analizzabile». Era un falso negativo di **riconciliazione dei pacchetti presenti**, non un risultato WUA.

## Nuovo percorso

- `PcSpecialistPortalSnapshot.Report.AllParsedPackages` trattiene **solo in RAM** fino a 2.048 nomi pacchetto già validati, non HTML grezzo, link S3 firmati, numeri ordine, token o seriali. La preview WPF resta limitata a 96 righe.
- `PcSpecialistLocalCatalogReader` pianifica ancora al massimo 24 sorgenti; poi seleziona una nuova preview dando priorità a **basenames esatti osservati durante l'importazione locale**. Prima i nomi realmente presenti, bilanciati per categoria; poi gli altri nomi validi, anch'essi equamente distribuiti.
- `MainWindow` sostituisce la preview dopo la riconciliazione, mostrando finalmente i match che erano stati omessi. Una nuova scansione o l'importazione HTML fallita azzera il set completo in RAM.
- La riconciliazione indica copertura parziale anche se una scansione INF ha escluso device PnP con produttori contraddittori (fix #385).

## Vincoli

Nessun fuzzy/substring matching, nessuna ricerca web, nessuna richiesta di numero ordine, nessun download, estrazione non autorizzata o installazione. Nessun nome di cartella privata compare nei sommari; la voce HTML resta solo un *nome file* non autenticato. Limiti invariati: 1 MiB HTML, 2.048 link, 96 righe UI, 24 sorgenti e 3.000 file locali. Confronti INF OEM sono solo evidenza per revisione; nessun giudizio su firma CAT, ranking SetupAPI, driver più recente o applicabilità.

## Test

Fixture con HTML contenente 170 voci chipset più NVIDIA e ZIP Killer fisico presente dopo le prime 96, verifica PnP INF reale e transizione da «non mostrato» a «dichiarazione INF locale». Test per ordine invertito, matching di soli nomi esatti, quote per categoria, limiti, dati privati e identità PnP ambigue. La validazione PCSpecialist Recoil 16 reale rimane #91/#136, Windows release gate #85 e firma Authenticode #283.
