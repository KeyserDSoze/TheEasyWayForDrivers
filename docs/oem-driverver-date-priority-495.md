# Confronto OEM locale: DriverVer data + versione (#495)

## Motivo
Un INF locale può avere un numero di versione più alto ma una data
`DriverVer` precedente a quella installata. La vecchia `LocalOemDriverComparison`
non leggeva la data: classificava il numero maggiore come potenziale
aggiornamento senza avvisare del conflitto. A parità di rank Windows
considera prima la data INF e poi la versione; il **rank del pacchetto
OEM esterno** non è disponibile da una scansione ZIP read-only.

## Implementazione
- Il campo INF viene interpretato solo con data calendario valida
  `M/d/yyyy` (CultureInfo.InvariantCulture) e versione dichiarata; date
  impossibili o campo incompleto non producono confronti falsamente nuovi.
- Si confronta la data INF con la data del driver installato, **solo quando
  entrambe sono presenti e attendibili**, preservando il giorno della data
  locale, senza uno shift involontario con conversione UTC.
- `Finding.DriverVerDateRelation` segnala Older/Equal/Newer/NotVerified.
  Se la data INF è anteriore, `IsPotentialNewer=false` anche con numero
  versione maggiore; il testo segnala il conflitto e rimanda a revisione
  manuale.
- La scelta del risultato visibile fra più INF per lo stesso dispositivo
  considera anche la data per evitare che un INF con data più vecchia
  nasconda la migliore prova di aggiornamento del bundle locale.
- Restano invariati ID PnP, TargetOSVersion, limiti, anteprima equa fra
  vendor e assenza di staging/installer/driver OEM in `Aggiorna tutto`.

## Limiti
La relazione data/versione è solo evidenza **diagnostica**. Non dimostra
il rank reale SetupAPI, firma CAT, compatibilità completa o necessità
d'installazione. Se la data del driver installato manca, il numero
versione superiore resta solo un indizio non verificato. Sono necessari
test hardware Recoil 16 (#136), confronto ZIP del proprio ordine (#120),
ranking e dipendenze (#130), firme INF/CAT (#128), benchmark (#91).
