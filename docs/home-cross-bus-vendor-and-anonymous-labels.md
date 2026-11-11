# OmegaDrive — schede Home NVIDIA/Realtek e report anonimo: identità PnP (#350)

## Difetto delle schede Home dopo #349

In v0.0.209, il confronto delle otto categorie Intel/Killer/NVIDIA
attribuiva correttamente gli hardware ID ai rispettivi produttori
tramite `PnpKnownVendorIdentity`. La schermata Home, invece, aveva
ancora un veto solo su `PCI\VEN` prima di classificare NVIDIA.

Un codec `HDAUDIO\FUNC_01&VEN_10EC&DEV_0295` (Realtek)
con provider installato obsoleto «NVIDIA» veniva contato nella
scheda **NVIDIA** invece di **Realtek**. Peggio, la scelta del
dispositivo per **Ispeziona Driver Store** usa la stessa funzione:
la selezione poteva quindi puntare al codec sbagliato. Lo stesso
errore era possibile con modelli USB, SWC e INTELAUDIO e con
identità PnP principali fra loro incompatibili.

## Correzione

La Home applica il riconoscimento del vendor per modello PnP
`PnpKnownVendorIdentity.FromDriver` **prima** del fallback
su nome/provider NVIDIA; la scheda NVIDIA è permessa solo
con vendor NVIDIA riconosciuto o vendor non riconosciuto,
mai con modello convalidato di un vendor diverso o
`Conflicting`. Il veto PCI preesistente è conservato.

La classificazione alimenta sia il conteggio delle tre schede
sia la selezione esplicita del PnP device per la lookup
**read-only** nel Driver Store locale. Non autorizza alcuna
installazione, non legge automaticamente nuovi cataloghi
e non trasforma un match esatto WUA in aggiornamento necessario.

I dispositivi GPU PCI NVIDIA, HD Audio HDAUDIO, componenti DCH
SWC/ACPI e modelli USB specifici VID_0955 continuano a essere
riconosciuti. I modelli Realtek audio rimangono nella
scheda **Realtek** anche con vecchi provider NVIDIA.

## Report anonimo produttori

Dopo #348, `VendorInventoryCoverage` riconosce Logitech/Razer
dai modelli HID/USB VID/PID; tuttavia il filtro privacy di
`ScanCoverageText` accettava solo altri otto vendor.
Ora anche le etichette **canoniche fisse** `Logitech` e `Razer`
sono consentite. Gli identificativi di istanza, nomi privati
e varianti arbitrarie (es. `Logitech PRIVATE_SERIAL`) continuano
a essere censurati come «altro produttore».

## Limiti

Si tratta di correzioni di presentazione/attribuzione locale.
I test automatici sono sintetici: **non è stato misurato un
aumento dei driver WUA/OEM installabili** né risolto il
confronto «8 Driver Easy vs 2 OmegaDrive» sul PCSpecialist Recoil
16 (#91/#136). Restano necessari QA Windows reale, fonti OEM
ufficiali, ranking e firme (#85/#128/#283).
