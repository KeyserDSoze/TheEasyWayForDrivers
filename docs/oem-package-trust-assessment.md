# OEM Trust Engine — prima fase (#79)

`OemPackageTrustAssessment` combina segnali **già disponibili e separati**: firma Authenticode del catalogo CAT, esito SignTool per i file campionati e anteprima delle dipendenze INF. Tre risultati: `Rejected`, `IncompleteEvidence`, `PartialEvidence`.

- Firma CAT valida senza membership file: **IncompleteEvidence**.
- CAT o file rifiutato: **Rejected**.
- CAT valido + file verificati + audit senza warning può produrre al massimo **PartialEvidence**.
- Nessun segnale, dipendenze sconosciute, file esclusi: **IncompleteEvidence**.

Ogni risultato mantiene `IsPackageVerified=false` e `IsInstallable=false`, inclusa `PartialEvidence`: firma e membership selettiva non dimostrano completezza, revoca, kernel policy, provenienza OEM o selezione di Windows SetupAPI. Non è stato aggiunto un percorso installazione. Il componente è **solo Core**, non ancora collegato alla schermata OEM né ai lettori nativi: richiede ulteriori test e integrazione prima di esporre report compositi per file locali (#46, #5).
