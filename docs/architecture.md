# Architecture

## Processes

The application uses three executables.

1. **App1.Service** is a Windows Service running with elevated privileges. It inventories devices, queries Windows Update for driver updates, performs selected driver installations and exposes service diagnostics.
2. **App2.Desktop** is one WPF process that owns both the main window and the notification-area icon. A separate tray executable is intentionally avoided because WPF and a WinForms `NotifyIcon` can coexist in the same process.
3. **App3.Setup** is the installer, updater and uninstaller. Updating cannot safely be delegated to the service executable while that executable is being replaced, so setup/update is a separate bootstrapper.

Shared contracts, device/update correlation, validation and release-checking code live in **Shared.Core**.

## IPC

Desktop and service communicate over the local named pipe `TheEasyWayForDrivers.Service.v1`. Requests and responses are newline-delimited JSON. The protocol supports:

- driver inventory;
- search for driver updates;
- install selected updates;
- real download/install progress messages;
- reboot-required result;
- application update checks;
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

Future provider adapters can add OEM-specific sources (NVIDIA, AMD, Intel,
Dell, Lenovo, HP, etc.) without changing the desktop/service boundary. Those
providers should reuse the same hardware-ID correlation layer rather than
matching by product-name text.

## Desktop dashboard

The desktop dashboard presents:

- installed device count;
- devices needing attention;
- number of available Windows Update driver packages;
- service health and version;
- a one-click complete check;
- per-device installed-driver details;
- correlated Windows Update package, provider, model, date and matching hardware ID;
- richer update-list metadata;
- service diagnostics and recent log lines.

The UI remains unprivileged. Privileged driver work stays in App1.Service.

## Update model

The service and desktop can query the GitHub Releases API. The desktop exposes
the user-facing update button. The downloaded setup executable is verified
against the release SHA-256 digest before it is started.

The setup executable performs file replacement, rollback and uninstall so that
running application files can be stopped and replaced safely.
