# OmegaDrive — getter opzionali COM WUA senza perdere un intero pass (#194)

## Difetto verificato in v0.0.92

Nel servizio `WindowsUpdateDriverProvider.SearchPass` diverse proprietà
facoltative erano lette con `SafeString(update.Title)`,
`SafeInt64(update.MaxDownloadSize)` e simili. **In C#, il getter COM
viene eseguito prima di entrare nella funzione `Safe*`.** Un singolo
getter `DriverVerDate`, `Description`, `DriverProvider` o
`DriverHardwareID` che lanciasse un'eccezione poteva invalidare
l'**intera ricerca** di una fonte, azzerando anche i GUID WUA validi
raccolti in quel pass.

## Modifica

- `WuaDriverCandidateReader` riceve il singolo update COM, legge
  ogni campo opzionale mediante un delegato il cui **getter è dentro**
  il relativo `try/catch`; la mancanza di un campo non cancella
  gli aggiornamenti già confermati.
- `Identity.UpdateID`, `SearchResult` e la collezione
  `Updates` restano strutturali: un errore su questi elementi
  invalida **l'intero pass**, come richiesto dalla policy
  all-or-nothing. Non si promuovono ricerche parziali a risultati
  completati.
- `DriverHardwareID` principale può non essere leggibile; l'update
  conserva il GUID e le associazioni effettivamente lette da
  `WindowsDriverUpdateEntries` e il relativo flag di incompletezza.
- `Description`, provider, produttore, modello, date e dimensioni
  mancanti **non** determinano da soli l'installabilità.
- I campi rilevanti per la sicurezza
  `Title`, `DriverClass`, `IsHidden`, `BrowseOnly` non sono mai
  sostituiti con assunzioni favorevoli. Se mancano o il getter fallisce,
  il candidato viene conservato per **verifica manuale**
  (`IsAdvancedCandidate=true`) e non appare in
  **Update per driver / Aggiorna tutto**.
- L'eventuale update Firmware/BIOS/UEFI rimane escluso dal gate
  `QuickUpdatePolicy`, con secondo controllo lato servizio in
  `WuaInstallRecheckPolicy`.
- Nessuna query aggiuntiva, registrazione WUA, nuovo driver scaricato,
  modifica del numero di GUID per entry o cambio dei privilegi del
  servizio.

## Regressioni e QA

Test del servizio con COM simulati: letture complete, errore di ogni
getter facoltativo, hardware ID primario non disponibile, associazioni
NVIDIA/Killer aggiuntive, campi critici non leggibili, BIOS/UEFI,
cancellazione propagata e identità UpdateID invariata.

Sul PC Recoil 16: confrontare prima/dopo il numero **aggregato**
di fonti WUA riuscite/parziali/fallite e i GUID Type=Driver rilevati,
senza pubblicare Device Instance ID, Hardware ID, GUID o dati di ordine
PCSpecialist. La correzione può recuperare update presenti in un pass
altrimenti scartato; **non** dimostra che Driver Easy segnali otto
pacchetti più nuovi, verificati o installabili. Rif. #91 #92 #136
#176 #188 #194.
