# OmegaDrive — recupero SetupAPI per USB/audio/SWC generici (#326)

## Perché
Dopo il filtro stringente degli ID generici WUA/INF (#324), un WMI
HardwareID non vuoto come `USB\\VID_8087`, `HDAUDIO\\FUNC_01&VEN_10DE`
oppure `SWC\\VEN_10DE` non è sufficiente per associare un driver.
Il fallback SetupAPI prima avveniva solo con HardwareID vuoto o un PCI
class/vendor-only; quindi ignorava gli ID specifici presenti nello
**stesso device** dell'inventario nativo Windows.

## Scansione read-only

`PnpNonPciModelRecovery` riconosce soltanto forme incompiute USB
VID/CLASS, HDAUDIO/INTELAUDIO FUNC+VEN o FUNC-only e SWC VEN.
Una lista WMI deve essere interamente composta da queste forme,
coerente per bus, vendor e funzione (se presenti); liste miste,
malformate o già complete rimangono autorevoli e non sovrascritte.

L'API SetupAPI nativa esistente riceve il **medesimo PNPDeviceID**
letto nella riga WMI, con limite di 2048 richieste per scan. I suoi
HardwareID sono usati solo se validati dal parser di modello completo
e se coincidono con bus/vendor/funzione osservati. Vengono conservati
anche gli ID WMI generici originali come evidenza, ma questi non
possono soddisfare il filtro di matching WUA/INF.

Nessun nome di dispositivo, seriale, percorso locale o contenuto COM
viene aggiunto al report aggregato. Non cambia la verifica del driver
CAT/OS, non vengono scaricati OEM EXE e `Aggiorna tutto` resta WUA.

Test sintetici USB Intel Bluetooth, NVIDIA HD Audio, Realtek IntelAudio,
SWC Intel/NVIDIA, class/funzione senza vendor, native di vendor e bus
sbagliati, suffissi malformati, mixed identity, PCI regressions e privacy.
Collaudo fisico Recoil 16 e confronto 8-vs-2 restano #136/#91.
