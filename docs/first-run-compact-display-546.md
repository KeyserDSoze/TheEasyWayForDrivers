# Primo avvio su display compatti e DPI elevato (#546)

## Riproduzione dal codice
La finestra `FirstRunWindow` era fissa a 680×570 unità WPF con
`ResizeMode="NoResize"`. Non avendo uno scorrimento centrale, un
monitor compatto o l'ingrandimento del testo Windows potevano
rendere inaccessibili le opzioni o la conferma iniziale.

## Modifica
- Il dialogo è ridimensionabile e massimizzabile (640×520,
  minimo 420×340).
- Solo le opzioni del primo avvio sono in uno `ScrollViewer`
  verticale automatico; la conferma resta nel footer separato.
- Nel footer istruzioni e bottone occupano righe diverse,
  evitando sovrapposizioni al restringimento orizzontale.
- Aggiunto un nome di accessibilità per scroll e conferma.
- Le tre preferenze, i loro valori iniziali e
  `ContinueButton_Click` non vengono alterati: nessuna
  installazione driver viene avviata automaticamente.
- Il gate UI `Test-GuidedResultsUi.ps1` verifica layout,
  contenuto, handler e persistenza delle preferenze.

## Limiti e prove
Il test statico e la compilazione Windows non dimostrano una
corretta esperienza visuale a DPI 100/125/150/200%, né
un collaudo Narrator/tastiera. Questo resta nella #95/#85.
