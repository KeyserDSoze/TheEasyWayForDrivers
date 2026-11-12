# WUA: modelli PnP major diversi prima di SUBSYS duplicati (#391)

## Gap verificato nel codice

Un singolo GUID Windows Update Type=Driver può dichiarare più di 1024 Hardware ID. OmegaDrive trasporta i primi 960 senza modifiche e distribuisce 64 slot ai vendor e bus PnP maggiori. Centinaia di SUBSYS/REV della stessa GPU NVIDIA o NIC Intel potevano occupare i posti riservati del bus PCI, lasciando fuori l'ID dichiarato più tardi di una RTX 4060 o Intel Killer. Questo è un falso negativo dell'associazione WUA→PnP, non una nuova offerta Windows.

## Nuova selezione

- Per NVIDIA, Intel/Killer, Realtek, AMD, Qualcomm, Broadcom e MediaTek: prima bus non ancora rappresentati; poi modelli pubblici bus+vendor+device diversi; solo dopo le revisioni/SUBSYS dello stesso modello.
- Le chiavi provengono esclusivamente dal parser di ID PnP rigoroso MicrosoftUpdateCatalogSearch.TryGetPublicModelKey. L'HardwareID originale completo, compreso SUBSYS, resta quello scelto per l'eventuale match esatto. Nessuna uguaglianza inventata da modello generico o nome OEM.
- Rimangono invariati i primi 960, i 64 slot, il massimo 1024 ID per GUID, il massimo 2560 entry lette per pass, l'ordine dei piccoli elenchi e la selezione equa tra produttori.
- La stessa scelta vale dopo il merge di due pass WUA confermati e non cambia il numero di GUID. Nessuna installazione, download, fonte nuova o bypass dei controlli QuickUpdatePolicy.

## Test

Regressioni sintetiche su centinaia di SUBSYS NVIDIA/Intel/Realtek, RTX 4060 e Killer negli ultimi elementi, HDAUDIO/SWC/ACPI, Qualcomm/Broadcom/MediaTek, SelectMerged, ID malformati e privati, matching PnP esatto e un solo GUID. Per validare se aumentano davvero i driver del Recoil 16 serve ancora QA Windows reale (#91/#111/#136). La firma Authenticode #283 non è coperta.