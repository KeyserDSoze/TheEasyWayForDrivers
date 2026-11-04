# Risultati OmegaDrive: UX e ricerca driver corretta (#113, #114)

## Sintomi osservati (v0.0.55)

La schermata Windows 11 era troppo densa: il pannello "Da verificare
con il produttore" mostrava l'intero inventario di handoff ufficiali
(molti dispositivi, non necessariamente update) in un secondo listone
sempre aperto e comprimendo la lista dei veri driver WUA.

Un bug indipendente nascondeva risultati reali di **Microsoft Update**:
la ricerca Comprehensive li registrava con
`SearchSource = Microsoft Update online`, ma la policy di visualizzazione
predefinita usava `CanQuickInstall`, che non accettava quella stringa.
Il candidato esisteva nei dati, ma non appariva come aggiornamento normale.

La dicitura "Analisi parziale" dipendeva anche da controlli ausiliari
(app update, diagnostica) che possono fallire senza impedire la
ricerca driver. Questi errori non devono essere presentati come
fallimenti del catalogo driver.

## Comportamento dopo la correzione

- **Risultati principali**: filtro iniziale `Tutti i risultati`.
  I candidati ordinari, Microsoft Update, facoltativi e avanzati
  rimangono visibili. Le azioni `Aggiorna` e `Aggiorna tutto` sono
  abilitate **soltanto** per update WUA ordinari con ID valido e
  origine WUA esplicitamente ammessa. Gli avanzati mostrano
  `Dettagli` e non sono selezionabili per il batch.
- **Produttore**: in primo piano compaiono soltanto pacchetti
  effettivamente valutati come necessari dai connettori ufficiali
  HP Image Assistant, Dell Command Update o Lenovo.Client.Update.
  Le mere fonti NVIDIA/Intel/AMD/Realtek vengono raccolte nella
  sezione **dispositivi con fonte ufficiale — non aggiornamenti**,
  chiusa per default. Si conserva il pulsante di ricerca OEM con
  consenso per HP/Dell/Lenovo.
- **Titolo/linguaggio**: mostra il numero dei **possibili**
  aggiornamenti individuati e separa candidati Windows da
  segnalazioni OEM. Se un servizio fallisce, dichiara ricerca
  non completata. Se una ricerca riesce e restituisce zero, dice
  soltanto che le **fonti consultate** non hanno proposto driver:
  non significa PC aggiornato.
- **UI**: la lista Windows occupa lo spazio flessibile della pagina;
  la sezione OEM è compatta e alta al massimo 240 px (con
  scorrimento quando aperta). Header più leggero, dati di versione
  WUA in una sola riga e meno avvisi duplicati.
- **Microsoft Update**: è riconosciuto nella policy quick-install
  solo con nome WUA esatto, ID GUID valido e flags ordinari.
  L'effettiva disponibilità è comunque ricontrollata dal servizio
  WUA prima di qualsiasi download. Nessun pacchetto OEM arbitrario
  passa al batch.
- **Diagnostica utile**: sotto Dettagli tecnici, `Copia report della
  ricerca` copia numeri per fonte WUA (inclusi ID aggiunti),
  sorgenti fallite/non registrate e conteggi per produttore.
  Il report non contiene numero di serie, nome PC, Hardware ID
  completi, Device Instance ID, download o percorsi locali.

## Regressioni automatiche

- xUnit `QuickUpdatePolicyTests`: MU ordinario valido sì;
  MU avanzato/nascosto, firmware/UEFI, guid non valido,
  sorgenti arbitrarie no.
- xUnit `CompactResultFilterTests`: default = all, MU
  visibile, avanzato visibile ma non aggiornabile, filtri
  opzionali e sorgenti sconosciute.
- xUnit `SimpleScanPresentationTests`: zero risultati
  non implica aggiornato, 8 dispositivi/2 WUA mostra 2
  candidati, stato parziale e sorgenti fallite onesto.
- xUnit `ScanCoverageTextTests`: report senza ID PnP,
  seriali, percorsi e fallimenti trattati come non verificati.
- CI Windows: nuovo `Test-GuidedResultsUi.ps1` verifica
  l'albero XAML, primo filtro `all`, Binding separati per
  OEM raccomandati e link, expander chiuso per default,
  layout flessibile e bottone copia report. Integrato in
  validate-pr e release, oltre a build .NET e suite xUnit.

## QA visuale e funzionale ancora necessaria

| Scenario | Atteso |
| --- | --- |
| 0 candidati WUA, 10 device NVIDIA/Intel | Lista centrale vuota con testo chiaro; 10 dispositivi solo nei dettagli OEM, non 10 update |
| 2 candidati WUA, Microsoft Update registrato | 2 candidati in "Tutti i risultati"; MU consigliato installabile via WUA; fonte e ID distinti nel report |
| WUA 2 ordinari + 4 avanzati | 6 in vista iniziale, 2 aggiornabili, avanzati solo "Dettagli" |
| Dell/HP/Lenovo senza tool locale | stato tool non disponibile, 0 update OEM confermati, accesso ai siti |
| Dell/HP/Lenovo con tool e 3 driver necessari | 3 righe OEM separate, niente "Aggiorna tutto" OEM |
| Microsoft Update non registrato | origine non interrogata, non conteggio fittizio 0 |
| Microsoft Update/WSUS fallito | titolo prudente, Aggiorna tutto disabilitato; candidati già ottenuti visibili |
| Update app fallito con WUA riuscito | **non** scrivere "analisi driver parziale" solo per l'app |
| Windows 100%/125%/150% DPI | Nessuna riga tagliata; testo e pulsante aggiornamento leggibili, due pannelli non occupano metà pagina |
| Full-screen e finestra minima 880×610 | lista principale sempre accessibile; footer e dettagli scrollabili |

**Limite reale:** non possiamo dedurre che Driver Easy abbia
ragione sui presunti 8 update o che NVIDIA pubblichi
automaticamente un nuovo driver tramite Microsoft Update.
Senza un catalogo NVIDIA ufficiale accessibile per dispositivo,
la diagnosi rimane più ampia della disponibilità verificata.
Il prossimo confronto sul PC deve usare il report aggregato e
versioni/identità dei pacchetti, senza pubblicare dati privati.

Riferimenti: #91, #94, #95, #110, #111, #113, #114.
