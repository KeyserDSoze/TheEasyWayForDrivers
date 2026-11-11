# OmegaDrive — supporto ufficiale Logitech / Razer per HID e USB (#348)

## Perché

Una periferica Logitech o Razer può comparire in Windows come
«HID-compliant mouse», «Generic USB device» o con provider Microsoft.
OmegaDrive v0.0.207 permette la ricerca *manuale* nel Microsoft Update
Catalog (#347), ma il pannello fonti OEM non classificava ancora quei
modelli in assenza di un nome esplicito del produttore.

## Regola di classificazione

`PnpKnownVendorIdentity` riconosce esclusivamente modelli PnP pubblici
con **Vendor ID + Product ID verificati**:
- `HID\VID_046D&PID_XXXX`, `USB\VID_046D&PID_XXXX` → Logitech
- `HID\VID_1532&PID_XXXX`, `USB\VID_1532&PID_XXXX` → Razer

La forma completa, inclusi eventuali `REV`, `MI`, `IG`, `COL`,
è validata dal parser di modelli pubblici (che per HID usa anche
`HidPnpMatchPolicy`). VID-only, usage senza PID, HID generici,
near-miss VID/PID, istanze serializzate e suffissi sconosciuti non
determinano l'OEM. HardwareID principale prevale su CompatibleID;
identità principali in conflitto producono `Conflicting`.

In aggiunta, `VendorSourceDiscovery` richiede un **contesto di bus
periferico HID/USB**. Un PCI, ACPI, audio o SoftwareComponent non può
trasformarsi in mouse Logitech/Razer solo perché possiede un CompatibleID
USB incoerente. Un nodo ROOT/SWD con sola compatibilità HID può avere
un link manuale solo quando la classe PnP è Mouse, Keyboard, HIDClass o USB.

## Esposizione

Per tali periferiche è mostrato soltanto un **handoff manuale** verso
due URL fissi del registro OEM:
- Logitech — `https://support.logi.com/`
- Razer — `https://mysupport.razer.com/`

`VendorSourceDiscovery.IsTrustedOfficialUrl` autorizza solo host
esatti via HTTPS sulla porta standard, senza credenziali nell'URL.
Né i path di istanza, né seriali, né nomi/comparazioni private
vengono aggiunti ai link.

Il conteggio WUA Type=Driver continua ad arrivare **soltanto**
dalle associazioni WUA filtrate e confermate; la riga OEM non
dichiara mai `IsVendorApplicable` o l'esistenza di un update.
L'apertura del supporto non scarica/installa driver e
non autentica cataloghi o firmware. Il ranking Windows, la
firma CAT/Authenticode e la compatibilità restano da verificare.

Il lavoro completa soltanto questo percorso di supporto in
#26 e #27, che rimangono aperte per l'integrazione completa.
Nessuna soluzione al confronto **8-vs-2** del Recoil 16 può
essere dedotta da questi test sintetici (#91 e #136).
