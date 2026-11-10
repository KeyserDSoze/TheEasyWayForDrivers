# OmegaDrive — fallback SignedDriver per Hardware ID WMI generici (#340)

## Problema
Se `Win32_PnPEntity.HardwareID` contiene solamente un token di classe o
vendor, la scansione tenta di recuperare il modello tramite SetupAPI della
stessa istanza PnP. Quando SetupAPI non restituisce alcun modello validato,
il dato `Win32_PnPSignedDriver.HardWareID`, già raccolto con join sul
`DeviceID` esatto, era ignorato perché WMI aveva una lista non vuota.

## Nuovo comportamento
Solo se tutti gli Hardware ID WMI sono riconosciuti *incompleti* dalle
policy esistenti e SetupAPI non fornisce alcun modello valido, il singolo
Hardware ID SignedDriver può precedere i token WMI generici. Si applica
lo stesso filtro rigoroso SetupAPI:
- PCI: modello VEN+DEV validato, con VEN WMI vincolante;
- USB/HDAUDIO/INTELAUDIO/SWC: modello completo con bus, vendor e funzione coerenti;
- HID: VID+PID completo, eventuale VID WMI vincolante;
- USBSTOR: tipo storage coerente e ID modello specifico.

ID malformati, instance path, altri bus/vendor, Hardware ID WMI già
completi, classi WMI in conflitto e modelli SetupAPI già validati non
sono sostituiti da SignedDriver. Gli ID compatibili e la priorità WMI
continuano a seguire le regole precedenti. Il flag
`PnpIdsRecoveredFromSignedDriver` distingue questa fonte da SetupAPI.

## Limiti e QA
È sola evidenza PnP locale, **non** una nuova ricerca WUA né una prova di
driver più recente, firma CAT, rank Windows o installabilità. Non modifica
`Update All`, download, staging, driver store o installer. I test sintetici
coprono NVIDIA/Killer, USB/audio/SWC/HID/USBSTOR, casi di conflitto,
precedenza, matching esatto e report anonimo. Per il confronto reale
Driver Easy 8-vs-2, Recoil 16 e firma pacchetti restano aperte #91,
#136, #128 e #283.
