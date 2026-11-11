# OmegaDrive — SNDW/ACP audio: match stretti e recupero della stessa istanza (#354)

## Problema

In OmegaDrive v0.0.213 la correlazione WUA/INF già esigeva modelli
specifici per PCI, USB, HID, HDAUDIO, INTELAUDIO, SWC e SoundWire SDCA.
I bus audio `SNDW` e `ACP` non avevano però una grammatica di
correlazione dedicata: il matcher generico accettava perfino
`SNDW\CLASS_00` e `ACP\DEVTYPE_0005` come identità esatte.
Questi token di classe/tipo non identificano un codec, e possono
comparire su più dispositivi. Inoltre il servizio non tentava la
lettura SetupAPI per WMI SNDW/ACP parziale, benché lo faccia già
per SoundWire SDCA (#353).

## Validazione conservativa

La nuova policy `ModernAudioPnpMatchPolicy`, riutilizzata dal matcher
WUA/INF e dall'arricchimento dello stesso DeviceID, considera
specifici esclusivamente:

- `SNDW\CLASS_XX&MAN_YYYY&PART_ZZZZ`, con eventuale **coppia**
  `&VER_XX&UID_XX`, senza suffissi ulteriori;
- `ACP\DEVTYPE_XXXX&DEVREV_YYYY&VEN_ZZZZ&DEV_AAAA`.

I campi sono numeri esadecimali a larghezza esatta; il bus e i
separatori devono essere validi. La policy non prova firma, versione
disponibile, ranking Windows o installabilità.

Solo i token WMI **parziali** ma riconosciuti
(`SNDW\CLASS_XX`, `SNDW\CLASS_XX&MAN_YYYY`,
`ACP\DEVTYPE_XXXX`, `ACP\DEVTYPE_XXXX&DEVREV_YYYY`,
`ACP\DEVTYPE_XXXX&DEVREV_YYYY&VEN_ZZZZ`) autorizzano una lettura
facoltativa e **read-only** degli ID `SetupAPI` della **medesima
istanza PnP**; in mancanza di modello nativo si può controllare il
singolo HardwareID già raccolto da `Win32_PnPSignedDriver` per
lo stesso DeviceID. Il modello completo recuperato deve soddisfare
il bus e *tutti* i campi WMI presenti: SNDW CLASS e MAN,
ACP DEVTYPE, DEVREV e VEN. Identità WMI multiple
contraddittorie, token malformed, altre bus, percorsi di istanza,
seriali, suffix arbitrari e modelli completi già disponibili
non attivano questo percorso.

L'ordine delle evidenze rimane:

1. modello HardwareID WMI completo;
2. recupero SetupAPI della **stessa istanza** se WMI è incompleto;
3. SignedDriver della **stessa istanza**, solo senza modello nativo valido.

Non sono state aggiunte richieste internet, cataloghi OEM,
download, installazioni o autorizzazioni `Update All`.
Il limite SetupAPI e gli altri budget IPC rimangono invariati.

## Test e limiti

Regressioni xUnit su codec con e senza VER/UID SNDW, codec ACP,
WMI generico, match INF e WUA Type=Driver, fallback SignedDriver,
precedenza WMI e SetupAPI, mismatch bus/vendor/classe/revisione,
suffissi e seriali malevoli, inventario molto grande e copia
diagnostica senza informazioni personali.

La CI prova soltanto dati sintetici. Restano da verificare i driver
audio realmente presenti sul PCSpecialist Recoil 16 e il confronto
con le otto voci Driver Easy (#91/#136). Una correlazione esatta
*non* significa che il driver sia nuovo, applicabile o firmato.
