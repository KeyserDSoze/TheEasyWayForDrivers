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
