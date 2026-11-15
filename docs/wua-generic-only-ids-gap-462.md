# Diagnostica WUA: offerte con soli ID generici/non verificabili — #462

## Problema e perché conta

Un servizio Windows Update può restituire un vero GUID `Type=Driver` con soli
identificativi **troppo generici per correlare una periferica specifica**, come
`PCI\\CC_030000`, `PCI\\VEN_8086`, `USB\\VID_8087` oppure un ID malformato.
OmegaDrive deve contare questa offerta realmente restituita, ma non può
attribuirla con onestà al dispositivo NVIDIA/Killer/Realtek del PC.

Fino alla #462 il report anonimo distingueva solo GUID senza ID e GUID non
attribuiti: mancava la spiegazione per le offerte i cui ID sono presenti ma
insufficienti per il matching. Un'assenza di match WUA **non certifica** un driver
installato aggiornato o una mancanza di pacchetti OEM.

## Contratto read-only

`WuaPnpAssociationAudit.Result.OnlyNonCorrelatableWuaIds` conta una volta ogni
**GUID con almeno un ID dichiarato, ma zero ID specifici validi** nel
sottoinsieme di primario + massimo 1.024 secondari che può attraversare l'IPC:

- Solo GUID Type=Driver originati da sorgenti WUA riconosciute e con pass
  realmente completato; le sorgenti fallite, sconosciute, Type=Software,
  cache storica e le stringhe non-GUID sono escluse.
- Le osservazioni dallo stesso GUID e più sorgenti completate vengono fuse
  prima del conteggio: se un secondo pass aggiunge un ID PnP specifico,
  quella riga non è più «solo ID generici».
- Il nuovo valore è un **sottoinsieme**, non un quinto gruppo:
  `ExactHardware + CompatibleOnly + GenericModelOnly + Unattributed`
  rimane uguale a `ConfirmedGuids`.
- Gli aggiornamenti senza neppure un ID dichiarato rimangono in
  `MissingWuaIds`, e quelli con uno specifico ID senza PnP coincidente
  rimangono «non attribuiti» ma non «solo generici».
- Con inventario PnP non verificato o quota diagnostica superata, non
  viene fornita una conclusione sulle attribuzioni. Un flag di ID WUA
  incompleti continua a indicare che altri ID non sono stati osservati.
- Il rapporto copiabile espone **solo numeri e frasi compilate nel codice**,
  mai il titolo, il GUID, il seriale, il dispositivo, il modello concreto,
  il percorso utente, l'ID Hardware/Instance o il nome WSUS.

Questo audit non installa nulla, non cambia il matching usato da Update All,
non attribuisce ranking SetupAPI e non dichiara applicabili pacchetti OEM.

## Test sintetici e gate reali ancora aperti

`WuaNonCorrelatableIdAuditTests` copre vendor/class-only NVIDIA, Killer,
Realtek, token con TAB, GUID privi di ID, modelli PnP non presenti, secondo
pass con un ID specifico, source fallita, USB/Bluetooth, review-only e
anonimizzazione della copia.

Serve ancora il benchmark reale sul PCSpecialist Recoil16 con versioni
installate, hardware ID conservati solo in locale e fonti Microsoft/OEM
autentiche: #91, #111, #136, #85. I risultati di Driver Easy non sono
automaticamente aggiornamenti firmati e compatibili con il PC.
