# OmegaDrive — recupero modello HID da SetupAPI (#338)

Quando WMI restituisce un ID HID di sola classe o il solo VID,
il modello PID non può essere correlato con i driver WUA/INF.
Il fallback SetupAPI ora legge in sola lettura il valore completo
dalla stessa istanza PnP Windows, senza cercare device per nome.

Il recupero è disponibile soltanto per classi HID conosciute e
VID con quattro caratteri esadecimali. I token nativi devono
essere HID VID+PID completi e con suffissi MI/COL/REV/IG validi.
Il VID originale, se presente, vincola anche il VID nativo.
Bus, VID estranei, suffissi incompleti, instance path e conflitti
fra più VID WMI sono rifiutati. Gli ID WMI completi restano prioritari.

Un ID nativo verificato precede i token WMI generici, mantenuti
come evidenza; non sono aggiunti cataloghi OEM o installazioni.
Il limite 2048 device/128 ID e la policy Update All non cambiano.

Test Core sintetici includono HID generico tastiera/mouse, Logitech,
vendor estraneo, ID malformati, PCI/NVIDIA e privacy del report.
Driver Easy 8-vs-2 (#91), QA hardware Recoil 16 (#136) e
firma Authenticode (#283) rimangono non verificati.
