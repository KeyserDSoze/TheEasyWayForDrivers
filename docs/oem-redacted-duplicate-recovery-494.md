# Precedenza sicura dei duplicati driver nei cataloghi OEM (#494)

## Difetto
HP CMSL, Lenovo.Client.Update e Dell Command Update forniscono collezioni
di pacchetti distinte dai driver WUA. Quando un primo record con ID
valido aveva titolo privato (es. percorso, mail o Instance ID), il parser
ne conservava la versione redatta `[dato riservato]`, ma segnava l'ID
come già usato. Un record driver successivo con **lo stesso ID** e
titolo pubblico NVIDIA/Intel/Killer/Realtek veniva scartato.

## Correzione
La deduplicazione mantiene l'ordine dei primi ID, ma sostituisce un
record con titolo `[dato riservato]` **solo** se un duplicato successivo
ha un titolo pubblico non redatto e supera già i filtri della fonte.
Vengono aggiornati insieme titolo, versione, data/severità e categoria:
la fairness HP/Lenovo usa quindi la categoria del record leggibile.

- Nessuna sostituzione fra due titoli leggibili: first-seen invariato.
- Un solo record privato resta presente ma redatto, non rimosso.
- Restano validi i gate tipo Driver; per Lenovo anche IsApplicable=true
  e IsInstalled=false; per Dell resta l'XML DTD-protected.
- Limiti 120 HP, 80 Lenovo, 150 Dell e budget delle query invariati.
- Nessun `DriverUpdateInfo`, installazione, staging, download, query
  remota aggiuntiva, inferenza di compatibilità hardware o firma.

## Regressione e limiti
Nuovi test coprono collisioni, categorie Graphics/Network dopo oltre
80/120 righe Audio, 150 driver Dell, privacy e categorie non driver.
QA su veri PC HP/Dell/Lenovo rimane necessaria (#106/#104/#100/#85).
Il gap Driver Easy 8-vs-2 sul Recoil 16 resta #91/#136.
