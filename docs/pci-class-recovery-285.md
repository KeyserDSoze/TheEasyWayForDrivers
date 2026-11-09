# Classe PCI PnP: fallback diagnostico per nomi Windows generici — #285

## Perché
Quando manca il driver OEM, Windows può presentare la GPU o la scheda di rete
come "PCI Device" con `PNPClass` vuota / `Unknown` / `System`.
OmegaDrive classificava NVIDIA/Intel GPU e Killer AX1675i solo se il nome
o la classe erano già noti, e i link Realtek/Qualcomm/Broadcom/MediaTek
potevano mancare pur in presenza dei veri ID PCI.

La documentazione Microsoft specifica le forme PCI vendor+device
`PCI\\VEN_vvvv&DEV_dddd` e i compatible ID `PCI\\CC_ccsspp`,
incluse le combinazioni con `VEN_`:
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/identifiers-for-pci-devices

## Politica conservativa
`PciClassIdentity` richiede contemporaneamente, **nello stesso record PnP**:
1. un modello PCI con `VEN_` e `DEV_` esatti e 4 cifre esadecimali ciascuno;
2. un codice classe PCI della categoria richiesta, esatto e con 4/6 cifre hex:
   display 0300/0302/0380 oppure rete 0200/0280;
3. classe WMI assente o generica (`Unknown`, `System`, `Other`).

Un `PCI\\CC_03` isolato, vendor inesatto o stringa contenente
`CC_03000` non autorizza la classificazione. Una `PNPClass` esplicita
come Camera o Bluetooth non viene sovrascritta. Killer AX1675i richiede
**ancora** il SUBSYS documentato e non è dedotto dal solo VEN_8086.

## Effetto
- Matrice aggregata otto righe: NVIDIA GPU, Intel grafica e Killer Wi-Fi
  visibili anche con nomi generici quando la classe PCI è nota.
- Support handoff solo HTTPS ufficiale per reti Realtek,
  Broadcom, Qualcomm/Atheros, MediaTek e Intel con classe PnP generica.
- Evidenze di versioni locali in Home per Intel/NVIDIA generici.
- Nessun aumento artificiale dei GUID WUA: correlazione per Hardware ID
  /Compatible ID **esatto** non cambia.
- Nessun install, download, firmware, staging, catalogo privato, ranking
  certificato, o modifica `QuickUpdatePolicy` / `Aggiorna tutto`.

## Test / QA
Regressioni su CC_0300/0302/0380, CC_0200/0280, classi generic/
contraddittorie, VEN/DEV/CC con prefissi simili, SUBSYS Killer esatto,
CompatibleID separato e report anonimo senza ID o seriali.
E2E sul Recoil 16 (issue #136) e confronto reale 8-vs-2 (#91)
restano aperti: classificare un dispositivo NON crea un nuovo aggiornamento.
