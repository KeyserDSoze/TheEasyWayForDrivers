# OmegaDrive — correlazione HID specifica e classe generica (#329)

I dispositivi HID spesso condividono gli stessi CompatibleID, tra cui
`HID\\HID_DEVICE`, `HID\\HID_DEVICE_SYSTEM_KEYBOARD` e
`HID\\HID_DEVICE_SYSTEM_MOUSE`. Per una diagnosi WUA/INF questi ID
**non identificano un dispositivo fisico**: la precedente uguaglianza
letterale poteva creare falsi match esatti in molte righe post-scan.

Ora il filtro comune di `DriverUpdateMatcher` esclude gli ID HID di
classe condivisa e i `HID\\VID_XXXX` privi di `PID_YYYY`, oltre
a percorsi di istanza e suffissi non documentati. Rimangono
correlabili per uguaglianza **integrale** i modelli specifici
`HID\\VID_XXXX&PID_YYYY` con attributi comuni `REV_XXXX`,
`MI_XX`, `COLXX`, `IG_XX`. Gli altri identificativi HID Windows
non VID/PID conservano il precedente matching esatto, previo controllo
della sintassi. Non si inferiscono ID, modelli o provenienza da titoli,
nomi o produttori.

Il filtro è condiviso da correlazione WUA→PnP, lista per dispositivo,
report anonimo e confronto INF mirato, senza installare OEM/INF o
modificare la policy WUA di `Aggiorna tutto`. I test sono sintetici:
la disponibilità concreta di driver NVIDIA/Killer/Realtek e il QA
Recoil 16 (#91/#136) rimangono da misurare su hardware reale.
