# Discovery+ Intelligence — valutazione prudente dei candidati

Il motore `DriverCandidateAssessment` spiega perché un aggiornamento **Windows Update** è associato a un dispositivo e se la sua versione numerica è confrontabile con quella installata.

## Evidenza identificativi
- **Hardware ID esatto:** corrispondenza stringa tra l'ID WUA dichiarato e gli Hardware ID presenti nell'inventario Windows.
- **Compatible ID:** corrispondenza più generica; richiede ulteriori verifiche.
- **Nessuna corrispondenza:** non attribuire il candidato al dispositivo.

Queste informazioni **non provano** che il pacchetto sia adatto a BIOS, revisione hardware, versione Windows o politica OEM.

## Confronto versione
Solo se il provider WMI installato e quello WUA sono uguali (ignorando maiuscole/spazi ai margini) si confrontano versioni di 2–4 componenti decimali: `10.1`, `30.0.100.1`. Altri formati, versioni mancanti e provider differenti risultano **non confrontabili**.

Stati: versione numerica maggiore, uguale, minore (possibile downgrade), non confrontabile. Una data più recente non è da sola prova di un driver migliore; versioni OEM personalizzate non sono confrontabili con un semplice ordine numerico tra produttori diversi.

## Limiti e sicurezza
- Per ora valuta il miglior candidato WUA correlato per ID nella scheda dispositivo; il ranking WUA esistente non viene modificato.
- **Non** blocca automaticamente un downgrade e **non** fa installazioni; segnala soltanto una verifica necessaria.
- I Driver Pack Dell, SoftPaq HP e risultati Lenovo richiedono evidenza per-device verificata, quindi non sono ancora valutati con lo stesso comparatore.
- Le valutazioni non vengono inviate a server terzi.
- Prima di introdurre automatismi servono prove su Windows 11 reale, firma digitale, policy OEM, rollback e verifica post-riavvio (Issue #5).

Riferimenti: Issue #36, #34, #9; PR #37.
