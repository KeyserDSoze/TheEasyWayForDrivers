# WMI senza HardwareID: recuperare un modello SignedDriver attraverso gli indizi vendor SetupAPI (#381)

## Perché alcuni dispositivi NVIDIA/Intel potevano sparire

La lista `Win32_PnPEntity.HardwareID` può essere vuota o inutilizzabile.
Se l'API nativa SetupAPI fornisce soltanto un ID
`PCI\\VEN_10DE&CC_0300`, `PCI\\VEN_8086&CC_0280`,
`HDAUDIO\\FUNC_01&VEN_10DE`, `SWC\\VEN_10DE`, ecc.,
un'implementazione precedente conservava il token generico ma
ignorava un modello completo già noto in `Win32_PnPSignedDriver` della
**stessa** istanza. Il token generico è volutamente non correlabile a
WUA/INF, quindi una GPU NVIDIA, una Killer o un componente audio
potevano non apparire nel report delle corrispondenze.

## Recupero bounded e conservativo

Solo se tutti gli HardwareID WMI sono assenti/non validi, tutti gli
HardwareID SetupAPI native sono incompleti e specificano **lo stesso
vendor**, e lo stesso SignedDriver contiene un modello completo
validato, `PnpSetupApiIdentity.Merge` può anteporre il modello
SignedDriver. Il bus, il vendor e per audio/SoundWire la funzione
devono essere coerenti. Restano anche i token originali SetupAPI;
le CompatibleID recuperate nella stessa situazione vengono filtrate
in base al medesimo scope.

Sono rifiutati i casi senza vendor esplicito (sola classe), bus e
vendor contrastanti, HardwareID WMI già specifici, native model già
specifico, stringhe SignedDriver private o malformate e liste native
troppo grandi. `UsedSetupApi` e `UsedSignedDriver` documentano
l'origine distinta del recupero.

Test su GPU RTX NVIDIA, Intel/Killer, HD Audio, USB, HID, SWC,
SNDW, SOUNDWIRE e ACP, inclusi casi negativi e report anonimo.

**Limite importante:** questa modifica NON interroga nuovi cataloghi,
non produce una nuova offerta WUA e non approva download/installazioni.
Un'identità PnP recuperata può permettere un matching di un GUID già
verificato, ma non garantisce che il pacchetto OEM sia più recente,
sicuro o adatto al notebook. Sono ancora necessarie prove reali
Recoil 16 e comparazione NVIDIA/Intel/Killer (#91/#136).
