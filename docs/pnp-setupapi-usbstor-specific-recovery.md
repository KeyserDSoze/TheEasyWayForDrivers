# OmegaDrive — recupero modello USBSTOR via SetupAPI (#339)

Le classi `USBSTOR\GenDisk`, `USBSTOR\Disk`, `USBSTOR\CdRom`
e `USBSTOR\RAW` sono identificativi compatibili condivisi,
non ID di uno specifico prodotto. Il filtro WUA/INF già li escludeva
ma WMI poteva restituire solo tali classi e impedire il recupero
del modello completo dalla stessa istanza locale.

Ora `PnpNonPciModelRecovery` abilita il lookup read-only SetupAPI
per i soli token class-only noti USBSTOR, senza usare friendly name,
seriali o periferiche diverse. Se WMI dichiara Disk, il modello
nativo deve essere USBSTOR Disk completo, validato dalla policy
UsbStorPnpMatchPolicy; i CdRom restano separati. RAW non
dichiara alcun tipo e accetta solo modelli USBSTOR specifici dalla
stessa istanza. Un set WMI con classi incompatibili fallisce chiuso.

Gli ID generici WMI sono conservati dietro l'ID nativo validato.
Un ID WMI già completo non viene sostituito, e le forme
vendor-only e malformate non autorizzano fallback su un nome simile.
Il budget 2048 istanze / 128 Hardware ID e Update All restano invariati.

I test sintetici coprono Seagate/Samsung/SanDisk, Disk/CdRom/RAW,
mixed class, matching esatto WUA e INF, near miss e privacy.
Un ID recuperato non prova aggiornamento disponibile, versione
più recente, firma CAT valida né installabilità. QA Recoil 16
e benchmark Driver Easy (#91/#136) restano aperti.
