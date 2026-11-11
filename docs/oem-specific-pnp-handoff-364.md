# Handoff OEM: modello PnP completo prima del produttore (#364)

## Difetto

Il percorso di fallback per Realtek, Broadcom, Qualcomm/Atheros e MediaTek
in `VendorSourceDiscovery.HasHardwarePrefix` accettava prefissi
`USB\\VID_XXXX` senza `PID_XXXX`, `HDAUDIO/INTELAUDIO\\FUNC_XX&VEN_XXXX`
senza `DEV_XXXX` e alcuni `PCI\\VEN_XXXX&DEV_YYYY&FAKE_..`.
Un dispositivo dal nome generico e con provider Microsoft poteva quindi
produrre un support handoff OEM basato su una identità incompleta o falsa,
mentre WUA/INF e il riconoscimento vendor rigoroso rifiutavano quel token.

## Intervento

Il fallback per i quattro vendor utilizza ora **solo** la chiave modello
non privata di `MicrosoftUpdateCatalogSearch.TryGetPublicModelKey`,
derivata da un ID integralmente validato. La corrispondenza al prefisso
di produttore avviene **dopo** la validazione completa. Per il bus PCI
occorre `VEN+DEV`; per USB `VID+PID`; per audio `FUNC+VEN+DEV`.
Suffissi inventati, ID d'istanza e vendor-only sono scartati.

Hardware ID e Compatible ID validati possono ancora determinare un
**collegamento di supporto manuale** anche con nome o provider Microsoft,
senza attribuire un update, versione o installabilità. Le regole separate
di precedenza fra vendor PCI e di corrispondenza WUA/INF non cambiano.

## Limiti

Le fixture testano i quattro vendor, nomi generici, ID incompleti,
suffissi falsi, path privati, Compatible ID e verifiche dei testi del
report senza seriali. Non provano che un driver OEM nuovo sia realmente
pubblicato per un PC specifico. Il benchmark Recoil 16 #91/#136, i
cataloghi per-device #100 e le firme dei pacchetti rimangono aperti.
