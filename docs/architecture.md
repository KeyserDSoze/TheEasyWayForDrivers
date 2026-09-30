# Architecture

## Processes

The first release uses three executables.

1. **App1.Service** is a Windows Service running with elevated privileges. It inventories devices, queries Windows Update for driver updates and performs driver installation operations.
2. **App2.Desktop** is one WPF process that owns both the main window and the notification-area icon. A separate tray executable is intentionally avoided because WPF and a WinForms \`NotifyIcon\` can coexist in the same process.
3. **App3.Setup** is the installer and updater. Updating cannot safely be delegated to the service executable while that executable is being replaced, so setup/update is a separate bootstrapper.

Shared contracts, models and release-checking code live in **Shared.Core**.

## IPC

Desktop and service communicate over the local named pipe \`TheEasyWayForDrivers.Service.v1\`. Requests and responses are newline-delimited JSON. The protocol supports:

- driver inventory;
- search for driver updates;
- install selected updates;
- progress messages;
- reboot-required result.

Before a production 1.0 release, the named pipe ACL must be hardened and every privileged command must be authorized explicitly.

## Driver sources

The initial provider uses Windows Update Agent (WUA) for discovery/download/install. This deliberately avoids scraping vendor web sites or the Microsoft Update Catalog.

Installed-device information is read from WMI classes \`Win32_PnPEntity\` and \`Win32_PnPSignedDriver\`.

Future provider adapters can add OEM-specific sources (NVIDIA, AMD, Intel, Dell, Lenovo, HP, etc.) without changing the desktop UI.

## Update model

The service and desktop can query the GitHub Releases API. The desktop exposes the user-facing update button. The downloaded setup executable performs the actual update so that running application files can be stopped and replaced safely.
