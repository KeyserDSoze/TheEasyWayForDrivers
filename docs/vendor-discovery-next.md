# Discovery multi-provider: evidence vs installability (#97)

OmegaDrive now displays **two explicitly separate groups after Analyze PC**:

1. **Windows Update results** returned by documented Windows Update Agent search
   passes; ordinary safe-to-offer IDs alone can use the existing explicit
   install confirmation and installer service.
2. **Da verificare col produttore**: installed hardware matched conservatively
   to official vendor sources (Intel Wi-Fi / Bluetooth / DSA, NVIDIA driver search,
   AMD). This is a support *handoff*, NOT proof that a newer compatible
   driver exists. No URL in this group is used to download/install automatically.

## Hardware evidence and links

- Intel graphics: Intel Driver & Support Assistant official page.
- Intel/Killer Wi-Fi (including Killer AX1675i): official Intel Wi-Fi driver
  download page (hardware family / OS / OEM model still must be checked).
- Intel/Killer Bluetooth: official Intel Bluetooth driver download page.
- NVIDIA GeForce RTX laptop GPUs, NVIDIA audio/framework: official NVIDIA
  driver search; check laptop OEM-specific customizations first.
- AMD Radeon or chipset where identified: official AMD support.
- Hardware ID correlates a WUA candidate to a device, but does not certify
  a vendor package. A WUA absence means OEM availability **unknown**.
- Official links are fixed HTTPS host allowlist only (Intel, NVIDIA, AMD,
  Dell and HP) and opened by explicit user action.

## Additional read-only catalog interrogations

| Host system | Query during Analyze PC | Result interpretation |
| --- | --- | --- |
| Dell PC with model | Dell published DriverPackCatalog.cab (already implemented reader) | Deployment Driver Packs matched to model, **not individual installable drivers**; verify OS and exact hardware |
| HP PC with locally installed HP CMSL | HP official Get-SoftpaqList -Category Driver (already implemented reader) | SoftPaq metadata for current PC; verify INF, device IDs, actual installed version |
| HP without CMSL | No query result, show missing tool | Not zero updates |
| Lenovo PC | Official Lenovo.Client.Scripting only in Advanced with verified four-character MachineType | No unverified automatic model guess; see Advanced |
| Intel, Killer, NVIDIA, AMD | Official product-specific support URL (not undocumented API) | Hand-off, vendor package status unverified |
| Other OEMs | Existing official OEM support entry under Advanced | No implied catalog query |

The catalog scan never downloads package executables. Dell downloads only
the published CAB for metadata and extracts a single XML via Windows expand.
HP invokes only the documented read-only PowerShell query using already
installed HP CMSL. No firmware, installers or BIOS changes.

## Limits and next QA

- This release does **not** claim to reproduce Driver Easy's eight suggestions.
  That comparison still requires real machine installed versions, Hardware IDs,
  Windows build, exact OEM model, original package catalog/signature/hash.
- Test on real HP/Dell systems and one notebook with Intel Bluetooth / Killer
  AX1675i / NVIDIA RTX Laptop GPU; inspect WUA overlaps, source handoffs and
  zero-source/error states. CI fakes do not replace this.
- Expand true vendor API/catalog enumeration only when published and supported;
  don't scrape private Intel DSA or NVIDIA App endpoints.
- Keep source status distinct from actual eligibility and automatic install.

Related: #89 #91 #92 #94 #95 #97.


## 4 novembre 2026 — maggiore copertura Hardware ID (#99, #100)

Questa fase **non aggiunge un database di versioni OEM**: aumenta la visibilità di
hardware riconosciuto e i percorsi di verifica, in una sezione **non installabile**
separata dagli aggiornamenti Windows Update. Gli aggiornamenti OEM pubblicati
non possono essere contati come «trovati» senza interrogazione/versione e verifica
di compatibilità specifica per il dispositivo.

| Famiglia | Riconoscimento read-only | Fonte proposta | Limite |
| --- | --- | --- | --- |
| NVIDIA GPU | `PCI\\VEN_10DE` | ricerca ufficiale NVIDIA | Nessun candidato NVIDIA verificato automaticamente |
| NVIDIA HD Audio | `HDAUDIO\\...&VEN_10DE` | ricerca NVIDIA | Nessun confronto con pacchetti audio NVIDIA |
| NVIDIA Platform Controllers | `ACPI\\NVDA...` | ricerca NVIDIA | Il package compatibile va verificato con il PC OEM |
| Intel / Killer | Intel PCI/USB, nome hardware/famiglia | DSA, Wi-Fi e Bluetooth ufficiali | DSA non copre ogni componente e gli OEM possono personalizzare |
| AMD | Radeon/display/chipset, PCI `VEN_1002` | supporto AMD | Nessuna enumerazione/versione pacchetti |
| Realtek | PCI `VEN_10EC`, USB `VID_0BDA`, HDAUDIO `VEN_10EC` | Realtek Download | Per audio OEM servono le personalizzazioni del produttore PC |
| Broadcom | PCI `VEN_14E4`, USB `VID_0A5C` | Broadcom Support Downloads | Disponibilità varia per prodotto e piattaforma |
| Qualcomm / Atheros | PCI `VEN_17CB` / `VEN_168C`, USB `VID_05C6` | dichiarazione Qualcomm | Qualcomm indica in genere il produttore PC come fonte driver |
| MediaTek / Ralink | PCI `VEN_14C3`, USB `VID_0E8D` | sito ufficiale MediaTek / OEM PC | Non è pubblicata in OmegaDrive un'API di versioni driver |

Per Realtek, Broadcom, Qualcomm e MediaTek i percorsi hardware sono usati
solo per dispositivi rete/Bluetooth (e audio dove applicabile), limitando
falsi positivi su altre periferiche dello stesso vendor. Le fonti sono host HTTPS
consentiti esplicitamente, non URL derivati dagli Hardware ID.

### Diagnostica su un PC reale

1. Eseguire **Analizza il PC** e distinguere «Aggiornabili con Windows Update»
   da «Da verificare col produttore» (compresi i componenti a nome generico).
2. Per ogni componente NVIDIA segnalato da un altro software, verificare che
   figuri in Windows PnP, il suo Hardware ID, il driver installato e la fonte
   ufficiale. Se non è in WMI, questa modifica non lo può inventariare.
3. Per versioni più recenti rispetto a WUA, confrontare la pubblicazione ufficiale,
   il modello notebook/OEM, OS/architettura, INF e firma: senza questi dati la
   versione «più nuova» non prova che l'update sia adatto.
4. Conservare **separati** esiti interrogazione/indisponibilità della fonte,
   versioni installate, metadati vendor e driver realmente installabili.

### Prossimi connettori ad alto impatto

Il passo necessario per aumentare gli **aggiornamenti OEM realmente trovati**
è integrare fonti per-device supportate e con risultato tracciabile. Priorità:
Lenovo con MachineType verificato e modulo ufficiale già installato; HP CMSL
per piattaforma; cataloghi Dell più granulari rispetto ai Driver Pack;
Microsoft Surface e fonti documentate degli altri PC OEM.
Per NVIDIA App, Intel DSA e AMD Auto-Detect servono interfacce **pubbliche
supportate**: non replicare traffico privato, scraping o credenziali di app.
I percorsi per vendor sono un piano di ricerca, non una promessa di API disponibili.

Fonti ufficiali:
- NVIDIA https://www.nvidia.com/en-us/drivers/
- Intel https://www.intel.com/content/www/us/en/support/detect.html
- Realtek https://www.realtek.com/Download
- Broadcom https://www.broadcom.com/support/download-search
- Qualcomm https://www.qualcomm.com/drivers
- MediaTek https://www.mediatek.com/company/contact-us
