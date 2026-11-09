# PCI vendor precedence: NVIDIA / Intel / AMD (#293)

## Difetto su v0.0.152
La correzione Killer #292 non si applicava ai rami NVIDIA, Intel o AMD.
Una GPU Intel con provider preesistente NVIDIA o una NIC Realtek con
etichetta NVIDIA potevano ottenere l'handoff alla fonte sbagliata;
analoga discrepanza per la scheda installata e le 8 categorie di #91.

## Correzione conservativa
- `VendorSourceDiscovery`: ramo NVIDIA/Intel/AMD solo se non esiste
  un modello PCI Hardware VEN+DEV valido di vendor diverso.
  `HasForeignPciVendor` è riutilizzato, privilegiando HardwareIds e
  ricorrendo a CompatibleIds soltanto in loro assenza affidabile.
- `VendorDriverEvidenceFinder`: fonte PnP riconosciuta anche per
  NVIDIA/Intel/AMD ha priorità su testo del provider installato;
  il fallback testuale non riintroduce vendor contraddetti da PCI.
- `ReferenceDriverCoverage`: stessa guardia per classificazione
  NVIDIA e Intel su dispositivo; WUA Type=Driver continua a essere
  associato esclusivamente mediante Hardware/Compatible ID esatti.
- Nessun cambiamento a riconoscimento HDAUDIO, ACPI NVIDIA, SWC o
  Killer AX1675i con sottosistema verificato.
- Se HardwareIds non include un vendor PCI valido (es. ROOT), un
  Compatible ID PCI Intel conserva l'handoff read-only. Se il
  principale dichiara un vendor PCI diverso (es. VEN_9999), quel
  Compatible ID non autorizza a dichiarare Intel come produttore.
- Test per conflitti NVIDIA/Intel/AMD/Realtek/Qualcomm, vendor coerenti
  fra handoff e installato, matrice 8 categorie e clipboard anonima,
  casi PCI near-miss e dispositivo senza ID PCI.

## Non obiettivi
Non si aggiungono nuovi cataloghi per-device o aggiornamenti.
`QuickUpdatePolicy`, `Aggiorna tutto`, WUA, firmware, download e
installazione rimangono invariati. Le issue #91/#136/#85
continuano a richiedere Windows Recoil 16 reale.
