# OmegaDrive · ricerca e spazio risultati guidati (#142)

## Perché

Dopo il restyling della PR #141 la lista WUA era virtualizzata, ma con una
finestra da 880×610 la sezione OEM restava sempre in vista e limitava lo
spazio per i risultati importanti. Inoltre un filtro poteva nascondere una
checkbox già selezionata: l'azione «Aggiorna selezionati» avrebbe comunque
incluso quella riga invisibile.

## Modifiche

- Ricerca inline nella barra della lista, per **nome driver, produttore,
  modello, classe e sorgente WUA**. I filtri «Consigliati / Facoltativi /
  Avanzati» e la ricerca si combinano.
- Solo campi utente non sensibili sono cercabili. Nessun hardware ID, device
  instance ID, percorso locale, seriale o testo descrittivo privato viene
  indicizzato o inviato a un server.
- Stato vuoto mirato: query senza corrispondenze, filtro categoria senza
  corrispondenze, scansione riuscita senza update e scansione non riuscita
  sono casi separati. Mai dichiarare «PC aggiornato» da una ricerca WUA vuota.
- La sezione **Produttori e fonti ufficiali** è un expander inizialmente
  chiuso. Si apre automaticamente dopo la scansione **solo se** un tool OEM
  ha realmente valutato driver consigliati; non per i semplici handoff.
  Rimangono separati «Driver individuati da Windows» e fonti/strumenti OEM.
- «Aggiorna selezionati»: soltanto i driver WUA ordinari selezionati e
  visibili nella ricerca/filtro corrente. Il riepilogo conta la selezione
  visibile.
- «Aggiorna tutto»: continua a includere **tutti** i candidati WUA ordinari
  consentiti anche se non visibili nel filtro. Tooltip esplicito; nessun
  cambiamento alla policy `QuickUpdatePolicy` o alla conferma installazione.
- Ogni nuova analisi svuota il campo di ricerca, reimposta «Tutti i
  risultati» e chiude l'area OEM prima di riempire i dati nuovi.

## Validazione

- Test xUnit di ricerca: campi previsti, case-insensitive, trim, query
  vuota, esclusione di Hardware ID e descrizione.
- Test xUnit di empty-state: filtro vuoto, nessun match e WUA incompleto.
- Script `tools/Test-GuidedResultsUi.ps1`: struttura WPF, eventi, tooltip,
  collassamento OEM e scoping dell'azione selezionati.
- CI su Windows con restore/build .NET 10 e suite xUnit.

## QA manuale ancora da effettuare

- [ ] Windows 11, finestra minima 880×610 e DPI 100/125/150/200%.
- [ ] 0/1/50+ update, titoli lunghi e ricerca/categoria in combinazione.
- [ ] Search senza match, premere «Nuova analisi» e verificare reset.
- [ ] Selezionare una riga, filtrarla fuori e verificare che «Aggiorna
      selezionati» non la includa; «Aggiorna tutto» resta globale.
- [ ] WUA offline/parziale, fonti OEM solo riconosciute e raccomandate,
      accesso agli strumenti di verifica/ZIP/CAB.
- [ ] Tab order, screen reader, high contrast e focus.
- [ ] Nessun collaudo di installazione su macchina primaria senza snapshot.

Questa PR non interviene sul divario di copertura 2 vs ~8 driver tra
OmegaDrive e Driver Easy (#91, #100, #136), ancora separato.

Collegamenti: #95 #94 #140 #142.
