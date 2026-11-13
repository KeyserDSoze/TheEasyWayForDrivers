# Cataloghi HP e Lenovo: selezionare DOPO il filtro (#284)

## Difetto e correzione
Il reader HP CMSL scartava le righe successive alle prime 120
**prima** di verificare `Category=Driver`. Analogamente, i reader
Lenovo Get-LnvUpdate e Lenovo.Client.Scripting si fermavano alle prime
80 o 50 righe prima di identificare driver necessari / PackageType=2.
Un catalogo con molti BIOS, firmware o software all'inizio poteva
nascondere NVIDIA, Intel/Killer e Realtek già elencati dal tool OEM.

Ora i tre reader visitano tutta la risposta JSON entro i limiti
esistenti (1 MiB, profondità 8), **scartano prima** tipi non driver e
righe incomplete; mantengono 120/80/50 righe massime **dopo** il filtro
e la deduplicazione per ID. Quando le righe realmente pertinenti
superano il limite, `OemCatalogResultSelection` fa round-robin per
categoria **dichiarata dal produttore** (Graphics, Network, Audio ecc.),
preservando l'ordine originale per cataloghi piccoli.

Solo Lenovo Get-LnvUpdate con `Type=Driver`, `IsApplicable=true` e
`IsInstalled=false` compare come **necessario secondo il companion**.
Il catalogo statico Lenovo e HP CMSL restano **metadati OEM pubblicati**,
non driver per-device verificati. Nessun cambiamento a WUA,
`QuickUpdatePolicy`, download, installazione, firmware o BIOS.

## Test e verifica
Fixture synthetic: 170 righe HP firmware prima dei driver;
130 righe Lenovo BIOS prima dei driver necessari; 75 righe
Lenovo catalog BIOS prima dei package Driver; 110-170 driver in una
categoria dominante prima di Graphics/Network/Audio; duplicati
case-insensitive; nessuna promozione di firmware.

L'aumento delle righe *OEM visibili* è possibile su cataloghi veri
affetti dal bug, **non** certifica driver aggiornabili né il gap
2-vs-8 Driver Easy su Recoil 16 (#91/#136). QA su HP/Lenovo reale
ancora necessaria in #85/#100/#106.

## HP CMSL: classificazione chiusa delle categorie driver (#425)

La selezione equa delle 120 righe può essere corretta solo se le righe sono **già classificate in modo sicuro**. In precedenza `HpSoftpaqCatalogParser` accettava qualunque testo con prefisso `Driver - `; un catalogo legacy o uno snapshot JSON non attendibile poteva presentare `Driver - Firmware`, `Driver - BIOS` o `Driver - Networkx` come driver e consumare gli slot delle categorie Graphics/Network/Audio. Il reader opt-in Desktop `HpSoftpaqCatalogReader` interrogava invece soltanto nove categorie HP esplicite.

Il parser usa ora **la medesima allowlist**: `Driver`, `Driver - Graphics`, `Driver - Chipset`, `Driver - Audio`, `Driver - Keyboard, Mouse and Input Devices`, `Driver - Enabling`, `Driver - Network`, `Driver - Storage` e `Driver - Controller`. Il confronto è case-insensitive, tollera soltanto spazi ASCII esterni e rifiuta caratteri di controllo e categorie diverse. Il campo Category viene letto dal JSON **prima** della sanificazione destinata ai testi mostrati, per non trasformare TAB/CR/LF in una categoria ufficiale. Una futura categoria realmente introdotta da HP dovrà essere aggiunta intenzionalmente **sia al reader sia al parser**, con test dedicato: i nuovi prefissi non sono accettati automaticamente.

Test sintetici verificano tutte le categorie ammesse, stringhe similari false, ID SoftPaq ripetuti, JSON con tipi non validi e una sequenza di 170 false righe Firmware prima di 130 driver input e dei casi GPU/Killer/Audio. Non cambia la natura dei SoftPaq: sono **metadati pubblicati**, non verifica di applicabilità o installabilità. QA reale CMSL su computer HP rimane aperta in #106, e confronto Recoil 16 in #91/#136.
