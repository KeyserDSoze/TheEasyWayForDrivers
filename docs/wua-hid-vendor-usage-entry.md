# OmegaDrive — WUA entry HID VID+usage rigorosi (#343)

## Problema

La correzione #342 ha ammesso l'ID di classe HID
`HID\VID_XXXX&UP:PPPP_U:UUUU` nei campi Windows PnP
e nel **primary** `DriverHardwareID` WUA. Questo ID non
contiene un PID, e quindi non identifica un prodotto installabile.

Ma l'altro lettore WUA, `WindowsDriverEntryIdReader`,
scartava ogni `:` ricevuto da
`IWindowsDriverUpdateEntry.DriverHardwareID`. In un pass
completamente letto, tale voce veniva erroneamente contata come
illeggibile/incompleta, anche se era un token HID valido.

## Risoluzione

Solo per gli ID contenenti `:`, il lettore entry riutilizza
`PnpIdentifierFallback.IsStrictSingleId`, che già verifica
l'unica grammatica HID VID+usage ammessa. Il vecchio filtro
per i valori senza `:` non cambia. Altri bus, ID classe,
path di istanza, suffissi sconosciuti e varianti malformate
restano invalidi e producono correttamente
`AdditionalHardwareIdsIncomplete`.

Distinguiamo tre nozioni:
- **Letto e valido** come dato WUA: l'ID VID+usage rimane
  nell'elenco con copertura verificata.
- **Correlabile**: `DriverUpdateMatcher` rifiuta la
  sola usage generica; se WUA dichiara anche il vero
  `HID\VID_XXXX&PID_YYYY`, soltanto questo può
  produrre un match specifico.
- **Installabile**: non modificato, sempre soggetto a
  policy e ricontrollo Windows Update; un ID generico
  non abilita nuove offerte, download o installazioni.

I test COM sintetici verificano normalizzazione,
dedup della primaria, near miss, false associazioni,
conservazione PID validi, numero GUID e audit anonimo.
Non costituiscono il confronto reale 8-vs-2 su
PCSpecialist Recoil 16 (#91/#136).
