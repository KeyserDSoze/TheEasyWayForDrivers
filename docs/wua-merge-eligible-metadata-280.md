# Preferenza WUA corretta tra fonti con lo stesso GUID (#280)

## Problema

Il merge delle ricerche WUA privilegia le rappresentazioni non-advanced,
ma una offerta `IsHidden=true` può essere non-advanced; inoltre una voce
con origine non riconosciuta potrebbe arrivare prima del risultato reale
`Windows Update online` / `Microsoft Update online` e nasconderne
i metadati eleggibili. Ciò produce una falsa classificazione `ReviewOnly`
per lo stesso GUID su Home e sui dettagli per dispositivo.

## Modifica conservativa

- Il merge sceglie prima la rappresentazione per cui
  `QuickUpdatePolicy.IsEligible` è vera: un pass WUA ordinario con
  GUID valido, fonte consentita, non nascosto/non avanzato/non firmware.
- Se entrambe sono eleggibili o nessuna lo è, conserva la precedente
  precedenza tra ordinaria e avanzata senza inventare disponibilità.
- Se **una qualsiasi rappresentazione** dello stesso GUID segnala
  firmware/BIOS/UEFI, il metadata risultante resta non installabile
  anche se l'altra rappresentazione ha un titolo generico.
- Gli Hardware/Compatible ID di altri pass **WUA riconosciuti** sono
  ancora uniti fino al cap 1024 come evidenza PnP. Una fonte
  sconosciuta non può aggiungere i propri ID a un'offerta eleggibile
  soltanto condividendone il GUID.

Nessuna modifica a `QuickUpdatePolicy`, `Aggiorna tutto` o al
recheck WUA del servizio in fase di installazione. Nessun pacchetto OEM
viene scaricato o installato.

## Test e limiti

Regressioni per ordine invertito di hidden/ordinary, origine sconosciuta,
conflitto firmware, associazioni per-device e GUID unici. La CI Windows
verifica compilazione e test; il benchmark reale Recoil 16 (#91/#136)
richiede il collaudo sul PC per documentare la copertura, non si deduce
dai test sintetici un incremento di offerte installabili.
