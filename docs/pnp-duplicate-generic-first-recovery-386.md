# Duplicati della stessa istanza: recupero conservativo di modello PnP completo (#386)

## Difetto

Nel report Win32_PnPEntity/WMI una stessa istanza DeviceId può avere più righe. La policy precedente scartava quelle con vendor contraddittori, ma sceglieva sempre la prima riga **anche se conteneva soltanto un ID class/vendor generico** e la riga successiva aveva un ID hardware completo. Questo occultava associazioni WUA/INF valide già presenti nell'inventario Windows; non equivale a una nuova offerta di driver.

## Soluzione sicura

`PnpDistinctTrustedDevices.Create` mantiene la regola fail-closed sui vendor contraddittori. Quando la prima riga ha già un HardwareID o CompatibleID **specifico e validato**, non la sostituisce mai. Altrimenti considera i modelli hardware completi delle righe successive dello **stesso DeviceId**, ma ne sceglie uno soltanto se:

- l'identificativo è PnP valido e correlabile, con un vendor riconosciuto;
- tutti i record con HardwareID specifici contengono lo stesso modello completo, senza vendor modello o CompatibleID discordanti;
- ogni altro record è privo di ID oppure contiene esclusivamente indizi generic/class-only sullo **stesso bus, vendor e funzione** compatibili con quel modello: PCI VEN/CC, USB VID/class, HID VID, HDAUDIO, SWC, SNDW o SOUNDWIRE.

Non fonde HardwareID/CompatibleID di righe diverse, non consulta cataloghi, non aumenta il numero di GUID WUA e non certifica versioni OEM, firme CAT o installabilità. Se gli indizi sono incerti, malformati, cross-bus, cross-vendor o ci sono modelli completi discordanti, rimane la riga originaria non specifica e la correlazione fallisce in modo conservativo.

## Regression suite

Test sui bus PCI NVIDIA/Intel Killer, HDAUDIO NVIDIA, SWC NVIDIA, SNDW Realtek, HID Logitech, USB NVIDIA; errori di vendor, classe, bus, ID di istanza, modello ignoto e compatible-only; convalida indiretta Home, inventario vendor, handoff, report WUA→PnP, ZIP INF OEM (stesso ordinale Windows originale) e privacy. Restano aperti QA Recoil 16 (#91/#136), E2E Windows (#85) e firma Authenticode (#283).