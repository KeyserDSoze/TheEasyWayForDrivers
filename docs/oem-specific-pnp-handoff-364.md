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


## Nome Windows mancante ma PnP riconosciuto (#405, 12 novembre 2026)

Un dispositivo rilevato da Windows può avere `Name`/friendly name vuoto o nullo pur possedendo un modello pubblico PnP completo (NVIDIA GPU/HD Audio/Platform, Intel Killer, Realtek audio e altri vendor supportati). Il confronto manuale `VendorSourceDiscovery.Find` **non lo elimina più automaticamente**: ammette la voce senza nome soltanto quando `PnpKnownVendorIdentity.FromDriver` riconosce un modello validato e non conflittuale; rimangono in vigore deduplicazione e veto su vendor discordanti in `PnpDistinctTrustedDevices`. Con dati insufficienti, il testo del produttore o del driver **non** può sostituire l'ID hardware.

Il testo della voce riutilizza `PnpDeviceDisplayName.Create` per un'etichetta **fissa e anonima per classe PnP** (per esempio «Scheda video (nome non disponibile)» o «Dispositivo audio (nome non disponibile)»), non il DeviceId d'istanza/seriale o un nome commerciale inventato. Il percorso `ChooseSource` e gli helper sono tolleranti rispetto a `Name=null`. I test sintetici verificano RTX PCI, NVIDIA HDAUDIO/ACPI/SWC, Killer SUBSYS, Intel, Realtek, Broadcom, casi negativi incompleti e conflittuali, corrispondenze WUA per ID e nessuna installabilità OEM attribuita. Si tratta solo di fonti di **consultazione**, non di un driver aggiornabile in `Aggiorna tutto`. La copertura Driver Easy/Recoil 16 e il collaudo hardware reale restano aperti (#91/#99/#136).
