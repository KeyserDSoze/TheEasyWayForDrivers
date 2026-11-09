# Vendor PnP: nome Killer non può sovrascrivere VEN PCI — #292

## Problema
Un DriverInfo con `Name = "Killer Wireless Adapter"` ma identificativo
hardware `PCI\\VEN_10EC&DEV_8125` (Realtek),
`PCI\\VEN_168C&DEV_003E` (Qualcomm) oppure
`PCI\\VEN_10DE&DEV_28A0` (NVIDIA) riceveva l'handoff Intel Wi-Fi.
Anche Home, versione installata e confronto otto categorie potevano
attribuire lo stesso hardware a Killer per la sola parola nel nome.

## Risoluzione
- `VendorSourceDiscovery.HasForeignPciVendor` legge SOLO HardwareIds,
  ripiegando su CompatibleIds se non c'è un modello hardware PCI
  affidabile. Convalida `VEN_hhhh&DEV_hhhh` (4 hex ciascuno), token
  singolo, confini esatti, mai `DeviceId`, nome, provider o seriale.
- Un hardware vendor diverso da `8086` impedisce il **routing Killer
  → Intel** per la singola istanza. La classificazione effettiva
  Realtek / Qualcomm / Broadcom / NVIDIA segue le esistenti sorgenti
  ufficiali PnP, in sola lettura.
- `VendorDriverEvidenceFinder.ResolveVendor` usa prima la sorgente
  PnP e non lascia che il solo titolo Killer scavalchi un vendor PCI
  documentato. Anche la matrice anonima delle otto categorie rifiuta
  NIC Realtek/Qualcomm erroneamente etichettate Killer.
- Un Killer AX1675i vero `VEN_8086&DEV_51F1&SUBSYS_16721A56`
  conserva la pagina Intel Wi-Fi e la riga Killer.
- Fixture con ID validi, vendor in conflitto, near miss VEN/DEV,
  privacy, Home e GUID WUA distinti.

## Sicurezza/limiti
Questi sono indirizzamenti alle fonti ufficiali, **non** offerte OEM
verificate. Nessun download, installazione, firmware, update automatico,
ranking o cambio `QuickUpdatePolicy`. Il confronto #91 (8 vs 2)
e la validazione sul Recoil 16 #136 restano aperti.
