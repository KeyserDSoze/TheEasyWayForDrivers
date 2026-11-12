# Anteprima delle fonti OEM: copertura parziale esplicita (#411)

## Problema

Nel post-scan OmegaDrive raggruppa i collegamenti OEM di sola **consultazione** e mantiene massimo 100 device visibili, con turnazione equa tra i vendor. Se l'inventario include molti dispositivi, il numero di riferimenti mostrati non era il totale dei riferimenti OEM riconosciuti, ma il limite non risultava evidente.

## Soluzione

`VendorSourceDiscovery.FindWithCoverage` restituisce le **stesse** righe selezionate da `Find` e anche il numero completo di dispositivi PnP **distinti, non contraddittori e con una fonte manuale riconosciuta** prima del limite, oltre al conteggio omesso. Il limite resta 100 nella Home (massimo consentito 150 per le chiamate tecniche) e la selezione round-robin dei vendor è invariata.

Quando il limite esclude almeno un dispositivo, una riga sempre visibile sopra l'expander OEM chiuso di default comunica: **«Anteprima OEM PARZIALE»**, conteggio visibile e totale, e numero non visualizzato. Senza omissioni il messaggio è nascosto. Il conteggio si azzera con una nuova scansione. La riga non contiene seriali, nomi WMI, DeviceId, Hardware ID o URL e non confonde i rimandi OEM con aggiornamenti installabili.

**I dispositivi omessi sono riconosciuti ma non mostrati nella sola lista di supporto manuale**: non significa che i driver non esistano, che siano aggiornati o che siano installabili. La ricerca WUA, i pacchetti OEM valutati necessari e le azioni «Aggiorna»/«Aggiorna tutto» sono indipendenti. QA reale Recoil 16 / benchmark Driver Easy rimane nelle issue #91, #114 e #136.
