# OmegaDrive — recupero di ID HID VID+usage Windows (#342)

## Problema e fonte
La documentazione Microsoft HIDClass descrive gli identificativi HID
dei top-level collections: modello vendor-specifico
`HID\VID_XXXX&PID_YYYY`, oppure un indizio **senza PID**
`HID\VID_XXXX&UP:PPPP_U:UUUU`.
Fonte: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/hidclass-hardware-ids-for-top-level-collections

Un WMI/SetupAPI HardwareID come
`HID\VID_046D&UP:0001_U:0006` è reale ma contiene due
due-punti; OmegaDrive lo scartava. Se questo era l'unico valore
WMI, si perdeva anche il VID del produttore, necessario per
vincolare il fallback dello stesso dispositivo.

## Implementazione
Il validatore locale accetta **soltanto** il token bus-qualified
`HID\VID_[4 hex]&UP:[4 hex]_U:[4 hex]`.
Gli altri identificativi con due-punti, i percorsi d'istanza
PnP, e i valori generici non qualificati
`HID_DEVICE_UP:0001_U:0006` restano scartati.

Il token è classificato come **incompleto**: non è mai un
identificativo hardware correlabile a WUA/INF né prova di prodotto
identico. Può vincolare al solo VID `046D` un eventuale ID
`HID\VID_046D&PID_C534` ottenuto dalle proprietà native
SetupAPI della **stessa** istanza PnP oppure dai metadati
SignedDriver associati all'identico DeviceID. Il PID rimane
obbligatorio. Gli Hardware ID WMI completi hanno precedenza.
Bus/VID contraddittori, usage malformate e ID PID stranieri
non producono recuperi.

Nessuna modifica a Windows Update Search, cataloghi online,
installazione, Update All o ranking/firma dei pacchetti.

## Test
Test automatici: decode REG_MULTI_SZ, WMI sanitize, token
vendor-only/usage combinati, PID native e SignedDriver, conflitti,
WUA/INF exact, report privato anonimo. Non è stato eseguito
collaudo fisico su Windows 11 PCSpecialist Recoil 16 (#136),
né un nuovo confronto reale Driver Easy 8-vs-2 (#91).
