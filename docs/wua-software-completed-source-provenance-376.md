# WUA Type=Software: provenienza verificata per ogni indizio (#376)

La selezione WUA Type=Driver richiedeva già che il pass COM della
specifica fonte fosse `Completed` (#373). Nel percorso separato
`Type=Software` (Killer Networking Software, Intel PROSet,
NVIDIA Platform Controller e altri) la UI/Desktop, la matrice
otto componenti e la copia report leggevano invece i record IPC
grezzi, **anche quando la loro fonte era fallita** oppure ignota.

La funzione `WuaDriverRelatedSoftwareClassifier.ConfirmedForReport`
è ora la selezione comune per Desktop, diagnostica e report copiabile:
- solo tre nomi fonte emessi da `WindowsDriverSearchPlan.CreateRelatedSoftware`;
- la **stessa** fonte del suggerimento deve aver concluso il suo
  pass `Completed`; stati duplicati/contraddittori non convalidano
  una fonte;
- ogni record, anche IPC legacy, viene nuovamente verificato da
  `TryCreate`: GUID valido normalizzato D, titolo di produttore
  riconosciuto, contesto pertinente, esclusioni BIOS/firmware,
  lunghezza e caratteri sicuri;
- stessa fairness 32 indizi per vendor quando l'input legacy è
  sovrabbondante, per non oscurare Killer/NVIDIA tardivi; un GUID
  in D/B/N/P entra una sola volta.

La vista continua a separare rigorosamente `Type=Software`
e `Type=Driver`. Nessun titolo viene associato a un PnP ID,
dichiarato installabile, passato a `Aggiorna tutto` o usato per
dedurre che il PC sia aggiornato. I pass falliti restano
diagnosticabili, senza divulgare computer/URL/ID/seriali.

Test: fonti Type=Software riuscite e fallite indipendenti
dalle fonti driver, GUID cross-source, titolo/ID non sicuri,
Killer/PROSet, conflitti e fairness. La QA reale #136 e il
confronto Driver Easy 8-vs-2 #91 restano aperti.
