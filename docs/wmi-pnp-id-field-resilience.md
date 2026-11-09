# OmegaDrive — getter WMI PnP HardwareID/CompatibleID resilienti (#323)

Nell'inventario di `Win32_PnPEntity` la lettura delle due proprietà
`HardwareID` / `CompatibleID` poteva interrompere l'intera enumerazione
se un singolo getter WMI/COM falliva. Una stringa scalare valida veniva
ignorata dalla precedente logica che accettava soltanto array.

`WmiPnpIdentifierFieldReader` recupera ogni categoria **indipendentemente**
dalla sua compagna. Accetta array a una dimensione fino a 128 elementi e
singoli identificativi bus-qualified; scarta le varianti non-stringa,
seriali/instance paths, valori malformati e liste oltre il limite tramite
`PnpSetupApiIdentity.SanitizeWmiIds`. Un getter WMI/COM/accesso non
disponibile produce una **categoria vuota**, non un inventario PnP vuoto.
Questo consente all'attuale recupero SetupAPI read-only per lo stesso
DeviceID esatto, con i vincoli di provenienza e precedenza già validati.
Cancellazione, errori di programmazione e fallimento dell'enumerazione
WMI obbligatoria restano errori espliciti.

I test sintetici coprono NVIDIA GPU, Killer Wi-Fi, Realtek audio,
duplicati, array misti, getter COM, scalare, limiti, privacy, isolamento
dei campi e assenza di abbinamenti WUA inventati.

**Limiti:** non vengono interrogati nuovi cataloghi OEM, non cambia la
compatibilità/verifica firma CAT e `Aggiorna tutto` rimane WUA-only.
Il gap reale Driver Easy (8 vs 2) e il collaudo su Recoil 16 restano
aperti (#91, #136). La firma Authenticode rimane #283.
