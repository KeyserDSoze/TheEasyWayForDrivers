# OmegaDrive — revisione grafica WPF, ottobre 2026

## Osservazioni verificate nel codice

La finestra principale utilizza un header scuro, card riepilogative, tab e DataGrid. I pulsanti Primary/Secondary avevano uno stile base senza trigger dedicati per hover, focus e disabled. La sezione Discovery+ contiene più flussi avanzati, tra cui l'analisi INF, l'audit e l'inspector OEM. La finestra dichiara dimensione minima 1040×650, che può limitare l'uso su schermi piccoli.

## Incremento UI #82

- Trigger visivi hover / focus tastiera / disabilitato coerenti sui due stili Button già utilizzati.
- Altezza minima e spaziatura dei pulsanti più consistenti.
- Card con raggio più morbido e righe/header dei DataGrid con respiro maggiore.
- Sezione OEM locale in Discovery+ con gerarchia esplicita e testo di cautela.
- Nessuna modifica a logica di ricerca, service, WUA, verifiche o installazione.

## Miglioramenti ancora da verificare

1. Test manuali su Windows 11 con scalatura 100/125/150/200%, contrasto elevato e navigazione da tastiera.
2. Reorganizzare Discovery+ in sottosezioni per ridurre scrolling e report tecnici affollati.
3. Controllare contrasto e stato selezionato dei TabItem; evitare colori che esprimano fiducia non dimostrata.
4. Migliorare accessibilità (AutomationProperties, screen reader, shortcuts) e ridurre la larghezza minima dell'app senza troncare controlli.
5. Allineare color tokens, dark mode e tipografia in un resource dictionary solo dopo QA UI.

## Release

`release.yml` produce automaticamente la versione patch successiva quando una modifica raggiunge `main` e la pipeline termina con successo. Non inventare un numero versione o presumere che una release esista prima che GitHub pubblichi gli asset e manifest.
