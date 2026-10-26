# OmegaDrive: schermata risultati compatta (#94)

Dal pulsante **Analizza il PC** si passa direttamente a una pagina risultati dedicata:
titolo e riepilogo, lista driver Windows Update virtualizzata, filtro
(consigliati, facoltativi, avanzati), versione locale solo con ID correlato,
versione proposta dove pubblicata, fonte e dimensioni, pulsante **Aggiorna**
per ogni riga, e footer persistente con **Aggiorna tutto** e
**Aggiorna selezionati**.

I dettagli WUA per ciascun pass, i provider ufficiali e le versioni rilevate
rimangono accessibili nel pannello espandibile; le otto schede precedenti
esistono ancora attraverso **Dettagli avanzati**.

## Confini di Update All

- Gestisce soltanto gli update **Windows Update** con Update ID valido.
- I candidati avanzati/nascosti, pacchetti firmware/BIOS/UEFI e sorgenti OEM
  non entrano nella selezione rapida.
- Il batch è disabilitato se una ricerca WUA è parziale o fallita.
  Le installazioni singole vengono comunque richieste con conferma esplicita
  ed eseguite esclusivamente mediante il servizio Windows Update.
- Anche «Aggiorna selezionati» scarta ogni voce non ammessa dalla policy.
- Aggiorna tutto non promette equivalenza con driver di altri software e
  non offre firmware o tool dei produttori come installabili.

## QA manuale da completare su Windows (#5, #85, #95)

- [ ] Apertura Home, analisi e passaggio immediato ai risultati, ritorno avanzato.
- [ ] 0, 1 e molti risultati; scrolling, DPI, tastiera e screen reader.
- [ ] Search WUA completa vs parziale/offline; pulsante Update All disabilitato
      sul parziale e fonte evidenziata.
- [ ] Update singolo: prompt, esito, stato riavvio e refresh dopo installazione;
      solo in VM con snapshot o dispositivo dedicato e consenso.
- [ ] Candidati firmware, avanzati/nascosti e OEM non ammessi al percorso bulk;
      regressioni della suite automatica.
- [ ] Selezione singola/multipla e filtri; pulsante Aggiorna selezionati usa
      solo gli elementi ordinari selezionati.

Non inventare mai versioni driver locali o online se non correlate per Hardware ID
oppure non presenti nei metadati della fonte.
