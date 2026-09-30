# OEM providers

## Principles

TheEasyWayForDrivers keeps Windows Update as the primary integrated source for
automatic driver discovery, download and installation.

OEM integrations must use documented, vendor-supported surfaces. The project
does not scrape vendor download pages and does not call undocumented local APIs
or private web endpoints.

The shared `IOemDriverProvider` abstraction lets the service add OEM-specific
status and workflows without changing the Windows Update provider.

## Intel

The first OEM provider is Intel.

The Intel adapter:

- identifies Intel devices from PCI vendor ID `8086`, USB vendor ID `8087`,
  and Intel manufacturer/provider metadata as a fallback;
- checks the normal 32-bit and 64-bit Windows uninstall registry views for
  Intel Driver & Support Assistant;
- reports the installed DSA version when available;
- exposes the official Intel Driver & Support Assistant support URL;
- never invokes Intel DSA localhost endpoints or reverse-engineered APIs.

Intel documents Driver & Support Assistant as the supported tool for scanning
Intel hardware and obtaining compatible Intel driver/software updates. The
desktop therefore performs an explicit handoff to Intel's official DSA flow
instead of attempting to impersonate or automate the private DSA backend.

## Future providers

NVIDIA, AMD and system OEM providers can implement the same
`IOemDriverProvider` interface.

A provider that later gains a documented machine-readable update API can add
discovery/download/install operations behind a separate capability contract.
Until then, companion-app handoff remains distinct from the integrated Windows
Update install path.
