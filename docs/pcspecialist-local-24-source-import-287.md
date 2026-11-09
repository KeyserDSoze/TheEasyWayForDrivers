# PCSpecialist: cataloghi locali fino a 24 sorgenti (#287)

## Difetto osservato
`PcSpecialistLocalCatalogPlan` e
`LocalOemArchiveBatchSummary` limitavano l'intera importazione
a 12 ZIP/CAB/EXE PE-ZIP/insiemi INF. Il tredicesimo pacchetto
bloccava tutte le evidenze, impedendo di confrontare una cartella
notebook che separa NVIDIA GPU/HD Audio, Killer/Intel Wi-Fi/Bluetooth,
Realtek, chipset e altri componenti in più archivi.

## Implementazione
- Limite coerente **24 sorgenti** nel planner, nel combinatore e nel
  dialogo di consenso WPF. La venticinquesima sorgente fallisce
  esplicitamente: non vengono nascosti risultati né dichiarata una
  verifica completa.
- Limite dimensione aggregata **4 GiB invariato**; massimo 3000 file,
  512 directory, profondità 16, 24 esami EXE PE-ZIP, 384 INF sciolti,
  limite per ZIP/CAB/INF, difese contro symlink/reparse/junction,
  timeout complessivo 5 minuti e annullamento invariati.
- Combiner continua a deduplicare per ordinal PnP anonimo, non per nome;
  22 copie del medesimo Realtek contano come 22 associazioni e
  **un solo dispositivo**, lasciando visibili NVIDIA e Killer negli
  archivi 23 e 24.
- Nuova prova con **24 ZIP veri su disco**: confronto INF read-only,
  match hardware esatto NVIDIA RTX 4060 / Killer e Realtek,
  `VersionRelation.NewerHint` soltanto suggerimento numerico; privati
  Device Instance ID, percorso e numero ordine assenti nel riepilogo.
- Test separato su 24 EXE PE-ZIP sicuri entro il cap e 25esimo INF
  rifiutato senza esecuzione.

## Non obiettivi e QA
Nessun accesso automatico al portale PCSpecialist o al numero d'ordine,
scraping, download/installazione, esecuzione EXE/MSI, update firmware
o modifica `QuickUpdatePolicy`. Un pacchetto locale non è firma CAT,
ranking SetupAPI o aggiornamento confermato. Il confronto Driver Easy
8-vs-2 (#91), il test sul Recoil 16 (#136) e l'E2E Windows #85
rimangono aperti.
