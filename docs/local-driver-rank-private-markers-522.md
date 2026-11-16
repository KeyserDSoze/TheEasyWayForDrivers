# PnPUtil / SetupAPI: marcatori seriali, ordini e clienti (#522)

## Problema
Nel pannello ranking Driver Store in sola lettura, le stringhe PnPUtil
erano redatte per Instance ID, URL, percorsi, email e `PRIVATE_`, ma
non per `ORDER_`, `SECRET_`, `SERIAL_`, `CUSTOMER_`. Una linea come
`Original INF: ORDER_ABC123.inf` poteva risultare leggibile e
rendere lo status `Available` con exit code 0, pur trattandosi
di un nome privato invece di una voce OEM pubblica.

La diagnostica SetupAPI aveva già `ORDER_` e `SECRET_` (issue #485),
ma non `SERIAL_` / `CUSTOMER_` nella lista di marcatori riservati
di `Description` e `ProviderName`.

## Correzione
- `WindowsDriverStoreEvidence.PrivateLineMarkers`: aggiunti
  `SECRET_`, `ORDER_`, `SERIAL_`, `CUSTOMER_`. La redazione continua
  a sostituire **l'intera riga** e controlla l'originale, rimuovendo
  solo in memoria gli invisibili Unicode per riconoscere marker
  divisi intenzionalmente.
- `WindowsSetupApiRankEvidence.PrivateNativeMarkers`: aggiunti
  `SERIAL_` e `CUSTOMER_`; la label nativa completa viene controllata
  **prima** del limite di 72 caratteri.
- Una sola riga `ORDER_...inf` privata non conta più come prova di
  driver enumerato; una `oem27.inf` indipendente rimane leggibile.
- Non si interpretano rank localizzati, non si scaricano o installano
  driver e non si classificano pacchetti OEM esterni.

## Test
`LocalDriverRankPrivateMarkerTests` verifica entrambi gli stati
di output PnPUtil, i quattro marcatori e casing alternativi, Unicode
invisibile, etichette native SetupAPI sensibili e marker dopo il cap,
oltre a casi legittimi NVIDIA / Intel Killer e PnPUtil italiano.

Restano aperte le prove su Windows reale (#132/#130/#85), il
confronto driver Recoil 16 (#136) e la firma Authenticode (#283).
