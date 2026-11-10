# OmegaDrive — WUA: altri produttori e periferiche HID fra gli ID tardivi (#334)

## Gap
Un update Windows può contenere più di 1024 Hardware ID dichiarati
nelle entry COM `IWindowsDriverUpdate4`. La selezione originaria
conservava i primi 960 e riservava 64 posti per NVIDIA, Intel/Killer,
Realtek, AMD, Qualcomm, Broadcom, MediaTek e SoundWire. Se gli ID di
un controller SSD, un produttore PCI poco comune o un mouse HID
specifico comparivano tardi, potevano essere scartati.

## Comportamento
Un **nono** gruppo protegge solo ID per modello specifico e controllato:
PCI VEN+DEV, USB VID+PID, HDAUDIO/INTELAUDIO FUNC+VEN+DEV,
SWC VEN+DEV e HID VID+PID. Il parser pubblico e la grammatica
HID già presenti validano i token; classe, VEN-only, VID-only,
suffissi anomali e path di istanza non accedono agli slot.

Dentro il gruppo, il primo ID di ciascuna famiglia bus/produttore
diversa precede le ripetizioni. Anche i produttori già rappresentati
nei primi 960 ID vengono marcati come tali: centinaia di entry
di una stessa VEN non nascondono una VEN, VID o HID diversa che
compare alla fine. La scelta finale resta deterministica e al
massimo 1024 ID, inclusi i due pass WUA riuniti. Non viene modificato
il numero di update GUID: gli ID ulteriori sono solo *evidenza di
correlazione*, non pacchetti extra o installabilità.

Regressioni sintetiche xUnit: Marvell/Samsung/Logitech e altri ID
PCI/USB/HID/audio, near-miss, tutte le otto categorie storiche,
merge di due fonti WUA, conteggi e limiti. Nessun accesso in rete o
download OEM. Recoil 16, Driver Easy 8-vs-2 (#91/#136), firme CAT
(#128) e Authenticode (#283) richiedono verifiche distinte.
