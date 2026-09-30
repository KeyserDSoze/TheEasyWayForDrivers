# Architecture

## Processes

The application uses three executables.

1. **App1.Service** is a Windows Service running with elevated privileges. It inventories devices, queries Windows Update for driver updates, performs selected driver installations, evaluates OEM provider status and exposes service diagnostics.
2. **App2.Desktop** is one WPF process that owns both the main window and the notification-area icon. A separate tray executable is intentionally avoided because WPF and a WinForms `NotifyIcon` can coexist in the same process.
3. **App3.Setup** is the installer, updater and uninstaller. Updating cannot safely be delegated to the service executable while that executable is being replaced, so setup/update is a separate bootstrapper.

Shared contracts, device/update correlation, OEM provider contracts, validation and release-checking code live in **Shared.Core**.

## IPC

Desktop and service communicate over the local named pipe `TheEasyWayForDrivers.Service.v1`. Requests and responses are newline-delimited JSON. The protocol supports:

- driver inventory;
- search for driver updates;
- install selected updates;
- real download/install progress messages;
- reboot-required result;
- application update checks;
- OEM provider status;
- service diagnostics and recent log retrieval.

The pipe has an explicit Windows ACL, blocks network identities, validates
privileged update-install requests and verifies that the connecting process is
the installed App2.Desktop executable under Program Files. See
[security-and-logging.md](security-and-logging.md).

## Driver sources

The primary provider uses Windows Update Agent (WUA) for discovery/download/install. This deliberately avoids scraping vendor web sites or the Microsoft Update Catalog.

Installed-device information is read from WMI classes `Win32_PnPEntity` and
`Win32_PnPSignedDriver`. Hardware and compatible IDs are retained so they can
be compared with WUA's `DriverHardwareID`.

The base inventory exposes statuses that can be established from the local
Windows state:

- `OK`;
- `Driver mancante`;
- `Errore Windows`.

After a Windows Update search, the desktop adds `Aggiornamento disponibile`
only when the WUA hardware/compatible ID can be correlated exactly to the
device.

## OEM provider layer

`IOemDriverProvider` is separate from `IDriverUpdateProvider`.

That distinction is intentional:

- `IDriverUpdateProvider` represents sources that TheEasyWayForDrivers can
  safely search/download/install directly;
- `IOemDriverProvider` represents vendor-specific integrations whose
  capabilities may be status-only, companion-app handoff or, later, a
  documented machine-readable API.

The implemented OEM adapters include Intel, NVIDIA and AMD plus a system-OEM detector based on `Win32_ComputerSystem`. The system layer recognizes Dell, Lenovo, HP, ASUS, Acer and Microsoft Surface, reports the model and exposes the manufacturer's official support flow.

The adapters deliberately avoid private local APIs, reverse-engineered
endpoints and page scraping. See [oem-providers.md](oem-providers.md).

The desktop also computes a per-device recommended source. An exact Windows Update hardware-ID match always takes precedence. Without such a match, firmware/system-specific internal devices prefer the recognized PC OEM; Intel/NVIDIA/AMD devices show both the chip vendor and system OEM when relevant; generic devices fall back to `Windows / OEM`.

## Desktop dashboard

The desktop dashboard presents:

- installed device count;
- devices needing attention;
- number of available Windows Update driver packages;
- service health and version;
- a one-click complete check;
- searchable/filterable inventory with attention-first sorting;
- colored status badges;
- per-device installed-driver details;
- per-device recommended driver source;
- correlated Windows Update package, provider, model, date and matching hardware ID;
- richer update-list metadata;
- OEM provider applicability and companion status;
- service diagnostics and recent log lines;
- deduplicated tray notifications when the discovered driver-update set changes.

The UI remains unprivileged. Privileged driver work stays in App1.Service.

## Update model

The service and desktop can query the GitHub Releases API. The desktop exposes
the user-facing update button. The downloaded setup executable is verified
against the release SHA-256 digest before it is started.

The setup executable performs file replacement, rollback and uninstall so that
running application files can be stopped and replaced safely.
