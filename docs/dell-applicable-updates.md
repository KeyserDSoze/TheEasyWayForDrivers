# Dell Command | Update: driver applicabili per PC corrente (#104)

## Motivazione

La scansione OmegaDrive tramite WUA può mostrare solo una parte delle
versioni OEM disponibili. Il Dell Driver Pack Catalog già integrato
interroga metadati di **pacchetti deployment**, non singoli update
necessari al computer. Dell Command | Update (DCU) ha invece un
comando documentato `/scan` che verifica il PC e può scrivere un
report XML degli aggiornamenti applicabili.

Documentazione ufficiale Dell:
- https://www.dell.com/support/manuals/en-us/command-update/dellcommandupdate_311_ug/command-line-interface-reference?guid=guid-92619086-5f7c-4a05-bce2-0d560c15e8ed
- https://www.dell.com/support/manuals/en-ie/command-update/dcu_rg/command-line-interface-error-codes?guid=guid-fbb96b06-4603-423a-baec-cbf5963d8948

## Funzionamento

1. Premere **Analizza il PC**.
2. Su un PC identificato come Dell, nella schermata dei risultati appare
   **Controlla i driver necessari con Dell**.
3. Con consenso, OmegaDrive cerca `dcu-cli.exe` in percorsi fissi
   sotto Program Files. Dell Command Update deve essere **già installato**
   e l'utente deve avere i privilegi necessari per la CLI.
4. Si invoca unicamente:
   `dcu-cli.exe /scan -updateType=driver -report=<file temporaneo> -silent`
5. Il codice di uscita 500 (documentato da Dell) significa **nessun
   aggiornamento con il filtro driver**. Un ritorno 0 con report mancante,
   troppo grande, XML errato o schema sconosciuto è una ricerca
   **non verificata**, non una conferma di zero driver.
6. Il parser accetta solo voci `Update` con Type esplicitamente
   `Driver`, ID e nome, con versione/severità facoltative;
   altri tipi sono ignorati. Lo schema XML varia tra versioni
   di DCU: un formato non riconosciuto richiede QA su PC reale.
7. I risultati sono metadati applicabili **secondo Dell**, mostrati
   nella sezione **Da verificare col produttore**, non nella lista
   installabile WUA. Non esiste alcuna chiamata `/applyUpdates`.

## Sicurezza e limiti

- Non installiamo DCU, non usiamo `/configure`, `/driverInstall`,
  `/applyUpdates`, non effettuiamo installazioni di driver, BIOS,
  firmware e non apriamo URL di download dei pacchetti.
- Percorso CLI da cartella Dell nota, no PATH o input utente.
- Directory temporanea univoca, cancellazione dei file prodotti,
  timeout di tre minuti e interruzione del processo su timeout.
- XML limitato a 1 MiB, DTD proibite, risoluzione esterna disattivata,
  limite sul numero di voci e dedup per ID.
- Un valore di `Version` non è automaticamente confrontabile
  con `Win32_PnPSignedDriver.DriverVersion`.
- La valutazione della compatibilità appartiene a DCU; OmegaDrive
  non valida autonomamente le dipendenze hardware/OS, le firme
  del payload o l'idoneità all'installazione.
- Il successivo tasto **Aggiorna tutto** continua ad agire soltanto
  sui normali ID Windows Update selezionati.

## Matrice QA rimanente

- [ ] PC Dell commerciale con DCU 5.x installato: confrontare UI e XML,
  verificare gli attributi reali delle voci e il filtro Driver.
- [ ] PC Dell senza DCU, privilegi insufficienti, senza rete, DCU in uso.
- [ ] Codici 0, 500, 501, 502, 503 e altri, report vuoto o non generato.
- [ ] PC non Dell: pulsante assente.
- [ ] Nuova scansione: righe DCU precedenti eliminate.
- [ ] Nessun BIOS/firmware/app viene proposto o installato.
- [ ] Nessun nuovo driver OEM viene incluso in Aggiorna tutto.

Questa funzione rappresenta un ulteriore connettore di **discovery**,
non una soluzione universale per ogni produttore.
