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
