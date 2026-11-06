# Discovery+ locale: confronto ZIP OEM e dispositivi Windows (#120)

## Perché

Sul PC **PCSpecialist Recoil 16** Windows Update può proporre solo
due driver anche se il produttore fornisce altri pacchetti NVIDIA,
Intel/Killer, Bluetooth, audio o componenti di piattaforma. La
presenza di un pacchetto **non prova** che debba essere installato
o che sia più recente. OmegaDrive aveva già verifiche INF,
Hardware ID, ZIP e platform-preflight ma solo negli strumenti
tecnici avanzati.

Con questa fase la schermata dopo **Analizza il PC** può
confrontare direttamente un **ZIP locale scelto dall'utente** con
i dispositivi PnP trovati sul computer. Non viene interrogato
un endpoint di download, né richiesto un numero d'ordine.

## Uso

1. Esegui **Analizza il PC**, anche se Windows Update non trova
   nuovi candidati.
2. Se si tratta di un PCSpecialist, apri **Driver PCSpecialist**.
   Il browser apre il portale ufficiale
   https://www.pcspecialist.co.uk/download-drivers/.
   Inserisci il numero d'ordine **soltanto nel sito del produttore**.
3. Salva uno ZIP del pacchetto driver ufficiale sul computer,
   senza eseguire alcun installer.
4. In OmegaDrive premi **Confronta ZIP driver** (visibile quando
   l'inventario dei dispositivi Windows è disponibile), seleziona
   lo ZIP e conferma la lettura.
5. OmegaDrive esamina in sola lettura fino a **96 file INF** e
   al massimo **24 MiB** di dati INF decompressi complessivi.
   Il formato ZIP è verificato contro traversal, doppioni,
   link simbolici e rapporti di compressione sospetti.
   Non viene eseguito codice, estratto un file o scaricato
   un pacchetto. CAB, EXE e MSI **non** vengono decodificati
   da questo flusso.
6. Per ogni INF con un modello PnP dichiarato l'app confronta
   gli Hardware ID e i Compatible ID, la decorazione
   `NTamd64/NTarm64` e la minima versione/build Windows
   con il sistema attuale.
7. Una riga per **dispositivo** evita di contare vari INF
   relativi allo stesso componente come aggiornamenti
   indipendenti. La riga mostra versione già installata,
   versione `DriverVer` proposta nell'INF e l'esito
   preliminare del controllo architettura/OS.
8. Se entrambi sono numeri di versione a **quattro parti**
   ed è un match Hardware ID esatto con sezione Windows
   coerente, l'app può mostrare **versione numericamente
   superiore da verificare**. Non significa che Windows
   consideri quel pacchetto migliore o installabile.
9. Il risultato compare sotto **Confronto ZIP locale — non
   installabile**, nella sezione del produttore, mai
   nell'elenco dei candidati Windows Update o nel pulsante
   **Aggiorna tutto**. Il conteggio degli update WUA
   non cambia.

## Modello di sicurezza e privacy

- **Firma:** leggere un file `CatalogFile` o trovare un .CAT
  nell'archivio non dimostra firma, autenticità o corretto
  binding tra INF/CAT/SYS. La firma deve essere verificata
  con Windows su file estratti in modo controllato in un
  successivo flusso esplicitamente autorizzato.
- **Compatibilità:** il confronto di ID/architettura/build è
  preliminare. SetupAPI, Windows driver ranking, dipendenze
  INF/servizi e release OEM restano autorevoli. Un numerico
  superiore non implica una revisione più sicura o supportata.
- **Riservatezza:** OmegaDrive non apre il portale per uno
  specifico ordine, non legge o memorizza il numero d'ordine,
  non carica l'archivio in rete e non esporta Device Instance ID
  o Hardware ID nel report aggregato. Il nome di file scelto
  non entra nella funzione di copia report.
- **ZIP:** non viene estratto né eseguito nulla. Un formato
  non supportato, uno ZIP malevolo o un INF indecodificabile
  non può essere interpretato come risultato di confronto
  valido vuoto. L'esito di fallimento invalida quello precedente.
- **Limiti:** dimensione archivio max 1 GiB, 2.048 entry
  d'archivio, 96 INF, 4 MiB per INF, 24 MiB aggregati,
  2.048 dispositivi PnP, massimo 80 dispositivi risultanti.
  La presenza di più INF relativi a un dispositivo non gonfia
  il conteggio.
- **Risoluzione:** il formato `DriverVer` viene letto come
  data,versione dall'INF e solo la versione a quattro parti
  viene confrontata; date, versioni mancanti, stringhe
  diverse, risultati da Compatible ID e architetture
  mismatch non diventano «nuove versioni».
- **Scope:** usare ZIP ufficiali per lo **specifico ordine**
  o configurazione PCSpecialist, ma l'app non può confermare
  la loro provenienza con la sola selezione del file.

## Test automatici

- [x] ZIP NVIDIA RTX4060: INF con ID esatto e sezione
  Windows compatibile, versione numerica superiore, ma
  `IsInstallable=false` e `IsVerifiedCompatible=false`.
- [x] Stesso ZIP con GPU e HDAUDIO NVIDIA: due dispositivi
  distinti, senza inventare un candidato WUA.
- [x] Doppio INF per stessa GPU: una sola riga.
- [x] Solo Compatible ID: comparazione numerica mostrata,
  ma non contata tra le versioni potenzialmente aggiornate.
- [x] OS troppo vecchio, architettura ARM64 sbagliata:
  nessuna indicazione di versione potenzialmente installabile.
- [x] INF con versione vecchia o uguale, INF senza ID
  corrispondente, ordine file nell'archivio.
- [x] CAB/EXE/MSI non supportati, ZIP con traversal,
  numero e dimensioni INF oltre i limiti, dispositivi
  troppo numerosi.
- [x] Test UI WPF: pulsante sotto la sezione OEM, nessun
  binding alla lista installabile, confronto attivato
  solo dopo inventario.

## Robustezza dei pacchetti multi-INF — correzione #192

Un singolo INF OEM con continuazioni troncate, una riga logica oltre
32 KiB o più di 64 continuazioni non deve cancellare le associazioni
valide presenti in altri INF dello stesso ZIP/CAB o della cartella locale.

- Il parser dell'**anteprima** esamina ciascun INF separatamente. Quando
  la sintassi non è attendibile, quell'INF viene conteggiato come
  **scartato**, senza utilizzare nemmeno gli ID apparsi prima dell'errore;
  il confronto continua sugli altri file.
- Un INF molto grande può superare la soglia di **500** ID mostrati
  nell'anteprima e richiedere un confronto mirato. Se la raccolta mirata
  supera **4.096** corrispondenze, il suo risultato viene interamente
  scartato e la copertura segnata come **parziale**; non si usano
  corrispondenze parziali per consigliare una versione.
- Il riepilogo distingue il totale INF, quelli scartati e quelli con
  dichiarazioni PnP valide. I warning aggregati non contengono percorsi,
  numeri d'ordine, Hardware ID o Device Instance ID.
- I controlli **globali** dell'archivio (traversal, duplicati, link,
  limite di decompressione, 96 INF, massimo **4 MiB per INF**,
  24 MiB totali e 2.048 dispositivi) continuano a rifiutare il
  **pacchetto intero** invece di degradare il risultato a «zero driver».
- Nessuna firma CAT, ranking SetupAPI, compatibilità reale, nuova offerta
  WUA o installabilità viene dedotta da un match locale.

Regressioni: ZIP con INF malformato più NVIDIA GPU/Intel Killer validi,
ZIP con solo INF malformato, cartella locale, e un INF con 4.200 ID
effettivamente mirati che supera il limite di 4.096. I driver validi
negli altri INF rimangono visibili; l'archivio con path traversal
resta rifiutato. QA reale del Recoil 16 ancora necessaria (#91/#136).

---

## Test hardware non ancora eseguiti

- [ ] Aprire sul Recoil 16 lo ZIP NVIDIA realmente pubblicato
  per la configurazione acquistata.
- [ ] Confrontare GPU RTX4060 Laptop, NVIDIA HD Audio,
  Platform Controllers e Wi-Fi Killer/Intel. Analizzare
  SUBSYS e architetture TargetOSVersion.
- [ ] Verificare che l'elenco INF del pacchetto sia
  completo e che una release con molti INF non superi i
  limiti di sicurezza.
- [ ] Confrontare versione proposta e driver di fabbrica
  con Windows/SetupAPI e tool OEM, non con il solo numero
  candidato di un software commerciale.
- [ ] Verificare leggibilità/scroll WPF su DPI 100/125/150%,
  e che *Aggiorna tutto* non includa mai i pacchetti ZIP.

La fase successiva dovrà gestire anche **CAB o EXE autoestraenti**
senza eseguire codice non attendibile, verificare autenticità
e firma dei pacchetti e valutare l'applicabilità reale. Per ora
è volutamente più sicuro rifiutare i formati non supportati.

Riferimenti: #120 #116 #91 #22 #100.
