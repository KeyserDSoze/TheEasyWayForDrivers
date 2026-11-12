# Recupero CompatibleID non-PCI: preservare bus e vendor del dispositivo (#379)

Nel recupero PnP read-only, Win32_PnPEntity può riportare HardwareID non-PCI
parziali: `USB\\VID_0955`, `HDAUDIO\\FUNC_01&VEN_10DE`,
`SWC\\VEN_10DE`, `SOUNDWIRE\\SDCA_06&MAN_025D` e classi USBSTOR.
OmegaDrive recupera un modello specifico dallo stesso nodo Windows
SetupAPI o dal metadato SignedDriver. Il codice precedente applicava lo
scope di bus/vendor/funzione al solo **HardwareID recuperato**;
`CompatibleID` nativi e SignedDriver potevano essere accettati senza
questo controllo quando il modello WMI non era PCI.

Ora ogni `CompatibleID` recuperato per un modello WMI non-PCI
incompleto deve essere un modello specifico validato dalla stessa policy,
oppure un token di classe generica esplicitamente riconosciuto e
compatibile con lo scope originale. Modelli di altri bus, vendor,
famiglie SoundWire o classi USBSTOR differenti non vengono copiati.
L'HardwareID recuperato convalidato continua a comparire e gli ID
compatibili originali WMI mantengono la precedenza esistente.

Test sintetici: USB NVIDIA/Qualcomm, HID Logitech/Razer, HDAUDIO
NVIDIA/Realtek, INTELAUDIO, SWC Intel/NVIDIA, SNDW/SOUNDWIRE,
USBSTOR, fonti SetupAPI e SignedDriver, matching WUA positivo/negativo,
identificativi generici e report anonimizzato.

Non sono state introdotte nuove fonti di driver, richieste di rete,
installazioni o download. La ricerca di driver effettivamente
applicabili sul Recoil 16 (#91/#136) richiede comunque QA Windows reale.
