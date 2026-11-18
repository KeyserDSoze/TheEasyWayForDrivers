# Risultati Home: footer accessibile e ricerca da tastiera (#556)

## Problema

Prima di questo incremento, il footer dei risultati WUA usava un
`DockPanel` con l'avviso di selezione e i due pulsanti di installazione
sulla **stessa riga**: font ingranditi o una finestra stretta
potevano ridurre lo spazio disponibile per l'avviso. La ricerca locale
nella lista virtualizzata non aveva una scorciatoia per raggiungerla
da tastiera dopo lo scorrimento.

## Nuovo comportamento

- Il footer rimane **sotto e fuori** dall'area scrollabile dei
  risultati, nel `Grid.Row=4`: il riepilogo selezione va a capo
  nella prima riga, le azioni stanno in un `WrapPanel` separato
  nella seconda, che può spezzarsi in presenza di poco spazio.
- La ricerca locale è raggiungibile con **Ctrl+F** solo quando
  `HomeResultsPanel` è effettivamente visibile e il campo è abilitato.
  Il testo esistente viene selezionato: non parte una ricerca online.
- Nel campo, **Esc** svuota il filtro attuale. Altrove non intercetta
  Escape, e nella Home prima della scansione Ctrl+F non sottrae il
  focus a eventuali altri controlli.
- Il riepilogo cambia con `AutomationProperties.LiveSetting=Polite`;
  nomi accessibili e tooltip differenziano `Aggiorna selezionati`
  (solo visibili e selezionati) da `Aggiorna tutto` (tutti i WUA
  ordinari, indipendenti dal filtro).
- `Test-GuidedResultsUi.ps1` valida DOM XAML, handler, visibilità
  e posizione delle azioni; resta il normale gate installativo WUA.

## QA residua

Il contratto statico più build Windows non dimostrano rendering,
focus Narrator o clipping reale a 100/125/150/200% DPI. Collaudi con
Desktop installato, tastiera e screen reader restano nelle #95 e #85.
Nessuna modifica a scansione, autorizzazione installazioni, WUA o OEM.
