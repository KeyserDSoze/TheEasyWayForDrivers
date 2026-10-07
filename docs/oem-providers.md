# OEM providers

## Principles

TheEasyWayForDrivers keeps Windows Update as the primary integrated source for
automatic driver discovery, download and installation.

OEM integrations use vendor-supported surfaces only. The project does not
scrape vendor download pages and does not call undocumented local APIs or
private web endpoints.

The shared `IOemDriverProvider` abstraction lets the service add OEM-specific
status and official handoff workflows without changing the Windows Update
provider.

## Intel

The Intel adapter detects PCI vendor ID `8086`, USB vendor ID `8087` and
Intel manufacturer/provider metadata as a fallback. It checks the normal
Windows uninstall registry views for Intel Driver & Support Assistant and
reports its installed version when available.

The support handoff opens Intel's official Driver & Support Assistant flow.

## NVIDIA

The NVIDIA adapter detects PCI vendor ID `10DE` and NVIDIA metadata. It checks
for the installed `NVIDIA App` and reports its version when available.

NVIDIA documents NVIDIA App as the companion for automatic driver updates for
gamers and creators. The support handoff opens NVIDIA's official driver page:

https://www.nvidia.com/en-us/geforce/drivers/

TheEasyWayForDrivers does not attempt to automate NVIDIA App internals.

## AMD

The AMD adapter detects PCI vendor ID `1002` and AMD/Radeon metadata. It
checks for AMD Software / Radeon Software in the normal Windows uninstall
registry views and reports the installed version when available.

AMD documents its Auto-Detect and Install / AMD Software Installer workflow as
the supported way to detect compatible Radeon and Ryzen chipset drivers. The
support handoff opens:

https://www.amd.com/en/support/download/drivers.html

TheEasyWayForDrivers does not scrape AMD driver pages or bypass the vendor
installer.

## System OEM

The service reads `Manufacturer` and `Model` from `Win32_ComputerSystem` and
maps supported manufacturers to official support workflows:

- Dell -> Dell Support / SupportAssist;
- Lenovo -> Lenovo Support / System Update;
- HP -> HP Support / HP Support Assistant;
- ASUS -> ASUS Support / MyASUS;
- Acer -> Acer Support / Care Center;
- Microsoft Surface -> Surface support / Windows Update.

The provider reports the detected system model and, when a classic Win32
companion can be identified reliably from the uninstall registry, its
installation state/version. Store-delivered companions are reported as
`Non verificato` instead of incorrectly claiming they are absent.

## Recommended source

For each local device the desktop computes a simple source recommendation:

1. `Windows Update` when a WUA driver package has an exact hardware or compatible-ID match;
2. the PC OEM for system-specific internal classes such as firmware/system components;
3. the hardware vendor plus the PC OEM for internal Intel/NVIDIA/AMD devices when both are relevant;
4. `Windows / OEM` for all other hardware.

This is a source-routing hint, not an automatic OEM installation. The user
still decides what to install.

## Future providers

System OEM providers such as Dell, Lenovo and HP can implement the same
`IOemDriverProvider` interface. A vendor that exposes a documented
machine-readable update API can later add direct discovery/download/install
capabilities behind a separate contract.


## MSI

OmegaDrive recognizes system manufacturers containing `Micro-Star` or the
`MSI` manufacturer name and routes them to MSI's official support flow.

When the installed-software registry exposes **MSI Center**, OmegaDrive reports
its presence/version. MSI documents **MSI Center Live Update** as an official
path for scanning, downloading and installing driver updates.

## GIGABYTE

OmegaDrive recognizes GIGABYTE system manufacturers and routes them to the
official GIGABYTE support flow.

When available in the installed-software registry, OmegaDrive detects
**GIGABYTE Control Center** (and the older GIGABYTE App Center naming).
GIGABYTE documents its Control Center / Update Center as an official mechanism
for keeping supported products and drivers up to date.

## Discovery cards

The desktop update page summarizes discovery in four groups:

- **Consigliati per il PC**: normal non-hidden WUA results;
- **Facoltativi**: WUA browse-only results;
- **Fonti OEM**: applicable official component/system vendor channels;
- **Avanzati**: hidden or comprehensive-search-only candidates.

The OEM count is a count of applicable official sources, not a claim that the
same number of OEM driver updates are available.
