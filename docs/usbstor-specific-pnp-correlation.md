# OmegaDrive — USBSTOR generici vs modello fisico (#335)

Il driver Windows USBSTOR.SYS genera **ID compatibili condivisi** come
`USBSTOR\\Disk`, `USBSTOR\\RAW`, `USBSTOR\\GenDisk` e
forme intermedie con solo produttore. Questi token non identificano
un prodotto: la loro uguaglianza letterale tra PnP, WUA e INF poteva
attribuire una stessa offerta a più dischi diversi.

Il filtro read-only `UsbStorPnpMatchPolicy` esclude i token condivisi,
vendor-only, malformati e i percorsi di istanza. Riconosce esclusivamente
modelli USBSTOR specifici con tipo, vendor padded 8 caratteri,
prodotto padded 16 e revisione quando dichiarata; comprende anche
gli ID untyped di Windows e la variante strutturata
`Disk&Ven_x&Prod_y[&Rev_z]` solo con entrambi i campi.
Non riduce mai la stringa: l'associazione WUA/INF resta per **ID
integrale** osservato sul dispositivo.

La regola è comune a Home, audit WUA→PnP e confronto INF locale, ma
non modifica i GUID WUA, la ricerca di update né le regole
`Aggiorna tutto`. Un match fisico non significa pacchetto più nuovo,
firma CAT valida o installabilità. I test sintetici verificano
classi USBSTOR, formati Microsoft, Seagate/SanDisk, INF, untyped,
compatible vs exact, near-miss, privacy e altri bus.

Specifica Microsoft: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/identifiers-generated-by-usbstor-sys

Il confronto Driver Easy 8-vs-2 (#91), la QA hardware Recoil 16
(#136), firme CAT (#128) e Authenticode (#283) restano aperti.
