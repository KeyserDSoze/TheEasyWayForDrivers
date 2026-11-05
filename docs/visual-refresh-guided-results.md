# OmegaDrive · Visual refresh della Home e dei risultati (#140)

## Scopo

La struttura dell'app rimane WPF / .NET. L'intervento riguarda il percorso
guidato di scansione e risultati, non il motore di discovery o l'installazione.

- **Home**: CTA unica «Analizza il PC» in un hero meno tecnico, descrizione
  breve e barra indeterminata visibile solo durante l'analisi.
- **Gerarchia**: card con più respiro e colori coerenti, risultati leggibili
  rispetto alle etichette di provenienza, bottone «Nuova analisi» secondario.
- **Righe WUA**: badge esplicito «Consigliato / Facoltativo / Avanzato /
  Nascosto», produttore e fonte su una riga distinta, versione senza
  inferenze; focus/hover e azioni di dettaglio conservati.
- **Stato vuoto**: distingue ricerca riuscita senza candidati, fallimento
  WUA, fonte parziale e filtro senza elementi. Nessuna frase «PC aggiornato».
- **OEM**: i comandi di confronto ZIP/CAB, cartelle e informazioni
  diagnostiche sono in «Strumenti e verifiche del produttore», chiuso di
  default. Gli aggiornamenti raccomandati da strumenti OEM rimangono in
  una lista separata; le fonti senza update nell'expander dedicato.
- **Footer**: mantiene «Aggiorna selezionati» e «Aggiorna tutto» WUA
  sempre distinti dalle fonti OEM. Prima di installare rimane la conferma.

## Garanzie mantenute

- «Aggiorna tutto» soltanto su ID WUA ordinari eleggibili dopo ricerca
  completa: `QuickUpdatePolicy` e conferma nel servizio non cambiano.
- Una scansione parziale non abilita «Aggiorna tutto».
- Firmware/BIOS, software OEM Type=Software e corrispondenze INF locali
  non compaiono nella lista installabile.
- Le versioni locali e remote restano solo evidenze comprovate, non stime.

## Checklist QA Windows

1. Windows 11 a DPI 100%, 125%, 150% e finestra minima 880×610:
   titolo, filtri, righe WU, footer e pulsanti leggibili; scrollbar verticale
   per gli strumenti OEM, nessuna sovrapposizione.
2. Home al primo avvio: una sola azione primaria; barra visibile durante
   l'analisi e nascosta dopo successo, fallimento o cancellazione.
3. Scansione con 0/1/20+ update: badge corretto, nome lungo NVIDIA,
   fonte Microsoft Update, wrapping e troncamento metadata.
4. Ricerca WUA offline/parziale/successo vuoto: messaggio corretto,
   «Aggiorna tutto» disabilitato sul parziale.
5. Sorgenti OEM senza update, OEM con raccomandazioni reali, Software WUA,
   ranking PnPUtil e confronto pacchetti: nessuno entra nel bulk.
6. Tab/Shift+Tab, focus giallo visibile, screen reader,
   hover e disabled; verificare anche tema ad alto contrasto.
7. Installazione su VM/snapshot e conferma esplicita, riavvio: test manuale
   separato, mai eseguito dalla UI statica.

## Validazione automatica

- `tools/Test-GuidedResultsUi.ps1`: struttura XAML e gerarchia,
  template e focus, progress state, empty state, separazione OEM/WUA.
- `CompactResultsEmptyMessageTests`: 5 scenari di copy prudente per
  zero risultati e copertura WUA incompleta.
- `.github/workflows/validate-pr.yml`: test PowerShell, build .NET 10
  e suite xUnit su Windows runner.

Il test statico non equivale alla review visuale reale sui dispositivi:
mantenere aperti i controlli DPI, accessibilità e high-contrast prima
della release.

Riferimenti: #94 #95 #91 #100 #136.
