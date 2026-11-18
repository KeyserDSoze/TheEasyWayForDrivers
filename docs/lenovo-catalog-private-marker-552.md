# Lenovo MachineType: privacy di ID e display OEM (#552)

## Gap
I metadati del catalogo ufficiale Lenovo `PackageType=2` sono una fonte
read-only, non una prova di applicabilità del pacchetto o della firma.
Prima del fix, `SafePackageId` poteva accettare chiavi ASCII locali
`SERIAL_...`, `ORDER_...`, `CUSTOMER_...` e inviarle alla vista
Discovery+ o a `OemPackageAssessment`. Il filtro dei quattro campi
esposti non riconosceva molti di questi marcatori e li perdeva se
separati da U+200B/U+2060 (Unicode invisibile).

## Correzione
- Denylist di marcatori privati e token PnP in identità Lenovo: **nessuna
  correzione, troncamento o output dell'ID respinto**.
- `Name`, `Version`, `Category`, `ReleaseDate`: ispezione dell'intero
  valore prima del limite a 160 caratteri, con soli formati Unicode
  invisibili rimossi nella copia di controllo privacy; al ritrovamento
  di un marcatore, display `[dato riservato]`.
- Deduplicazione con preferenza per una seconda descrizione pubblica
  dello **stesso** ID se la prima era redatta, mantenendo il primo
  dato quando entrambe le descrizioni sono già pubbliche.
- Validazione anche sui record `Package` creati direttamente, nei
  percorsi `DiscoveryResultAdapter` e `OemPackageAssessment`.
- I normali nomi multilingue e gli ID sintetici Lenovo validi restano
  visibili; la risposta non autorizza installazioni OEM.

## Limiti
Nessun modulo Lenovo avviato dal test e nessun driver installato.
Richiede ancora test su PC Lenovo reale, query del catalogo completo
e QA WPF/Windows 11 (#102, #100, #85).
