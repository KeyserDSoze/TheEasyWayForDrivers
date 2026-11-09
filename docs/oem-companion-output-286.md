# HP / Lenovo: evitare troncamento PowerShell a monte del parser — #286

## Problema
La fix #284 sceglie in Core fino a 120 righe HP e 80 righe Lenovo
**dopo** filtri e selezione equa per categorie, ma il Desktop continuava
a inviare solo 120/80 righe dei companion PowerShell. NVIDIA, Killer e
Realtek oltre questi offset non raggiungevano mai il parser.

## Correzione
- I due comandi PowerShell opt-in conservano fino a **1000 risultati
  filtrati** e poi passano i metadati al parser. 1001 righe generano
  **errore di copertura**, non un catalogo completo finto.
- HP mantiene solo le categorie `Driver` / `Driver - ...`
  dichiarate da CMSL; Lenovo mantiene `Type Driver`,
  `IsApplicable=true`, `IsInstalled=false`. Il nome del pacchetto
  non autorizza mai la classificazione come driver.
- Il parser C# rimane l'unico responsabile del cap finale 120/80
  con fairness/duplicate-ID.
- `OemCompanionOutputReader` limita **durante la lettura**
  stdout a 1 MiB di caratteri e stderr a 16 KiB. Una risposta
  sovradimensionata interrompe il companion e non carica l'intero
  output arbitrario nella memoria del Desktop. Cancellazione/timeout
  e nessun logging di output originale restano invariati.
- Il gate CI `Test-OemCompanionExport.ps1` esegue le query reali
  estratte dal sorgente contro un modulo Lenovo simulato e una
  funzione HP simulata su WindowsPowerShell. Verifica driver NVIDIA e
  Killer in coda dopo 130 voci Audio, nonché l'overflow >1000.

## Limiti e dipendenze
Sono **dati dal companion OEM**, non pacchetti verificati da OmegaDrive
per installazione. I companion richiedono installazione/consenso e
possono fare verifiche proprie con privilegi e accesso rete; nessun
installer eseguito da OmegaDrive, nessuna modifica firmware, download
o `QuickUpdatePolicy` / `Aggiorna tutto` OEM. Se il catalogo
contiene più di 1000 driver la copertura resta esplicitamente non
verificata (anziché perdere righe silenziosamente). QA reale sui
computer HP/Lenovo: #85, #102, #106. Recoil 16 #136 e gap #91
restano aperti, dato che questi sono cataloghi OEM diversi.
