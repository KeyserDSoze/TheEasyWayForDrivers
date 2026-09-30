# Architecture

## Processes

The application uses three executables.

1. **App1.Service** is a Windows Service running with elevated privileges. It inventories devices, queries Windows Update for driver updates, performs selected driver installations and exposes service diagnostics.
2. **App2.Desktop** is one WPF process that owns both the main window and the notification-area icon. A separate tray executable is intentionally avoided because WPF and a WinForms `NotifyIcon` can coexist in the same process.
3. **App3.Setup** is the installer and updater. Updating cannot safely be delegated to the service executable while that executable is being replaced, so setup/update is a separate bootstrapper.

Shared contracts, models, validation and release-checking code live in **Shared.Core**.

## IPC

Desktop and service communicate over the local named pipe `TheEasyWayForDrivers.Service.v1`. Requests and responses are newline-delimited JSON. The protocol supports:

- driver inventory;
- search for driver updates;
- install selected updates;
- real download/install progress messages;
- reboot-required result;
- application update checks;
- service diagnostics and recent log retrieval.

The pipe has an explicit Windows ACL, blocks network identities and validates privileged update-install requests. See [security-and-logging.md](security-and-logging.md).

A future production-hardening step can bind privileged requests to the user who installed/owns the desktop session instead of allowing any local interactive session.

## Driver sources

The initial provider uses Windows Update Agent (WUA) for discovery/download/install. This deliberately avoids scraping vendor web sites or the Microsoft Update Catalog.

Installed-device information is read from WMI classes `Win32_PnPEntity` and `Win32_PnPSignedDriver`.

The inventory exposes only statuses that can be established from the local Windows state:

- `OK`;
- `Driver mancante`;
- `Errore Windows`.

Available Windows Update driver packages are shown separately until a reliable update-to-device correlation is implemented.

Future provider adapters can add OEM-specific sources (NVIDIA, AMD, Intel, Dell, Lenovo, HP, etc.) without changing the desktop/service boundary.

## Desktop dashboard

The desktop dashboard presents:

- installed device count;
- devices needing attention;
- number of available Windows Update driver packages;
- service health and version;
- a one-click complete check;
- per-device details;
- service diagnostics and recent log lines.

The UI remains unprivileged. Privileged driver work stays in App1.Service.

## Update model

The service and desktop can query the GitHub Releases API. The desktop exposes the user-facing update button. The downloaded setup executable performs the actual update so that running application files can be stopped and replaced safely.
