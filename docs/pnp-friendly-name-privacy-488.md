# FriendlyName PnP: privacy email e percorsi slash (#488)

## Problema

Il campo WMI `Name` può contenere dati locali anziché un nome commerciale.
Il precedente filtro bloccava i PnP ID con backslash e gli URL `://`,
ma ammetteva `C:/Users/PRIVATE_OWNER/driver.inf`, email e nomi
con marcatori privati; la vista OEM può ricevere anche snapshot IPC legacy.

## Correzione in sola presentazione

- `PnpDeviceDisplayName.Create` applica un fallback per email, `:/`, `//`,
  marcatori PnP/istanza/seriale, marcatori `PRIVATE_`/`SECRET_`/`ORDER_`,
  Unicode invisibile, whitespace anomalo e U+FFFD.
- Nomi commerciali ordinari, accentati e con slash singolo restano visibili.
- Classi PnP non riconosciute producono il fallback generico fisso.
- `DeviceId`, Hardware/Compatible ID e matching WUA/INF non sono alterati.
- Nessun download, lookup remoto, selezione o installazione di driver.

## Test e limiti

`PnpDeviceDisplayNameTests` include regressioni di privacy, nomi validi e
handoff OEM a partire da un nome ostile di snapshot legacy. I test sintetici
non sostituiscono il gate Windows reale (#85, #5) né le verifiche hardware (#136).
