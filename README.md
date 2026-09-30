# TheEasyWayForDrivers

TheEasyWayForDrivers is a Windows utility for checking installed hardware drivers, identifying devices that need attention, finding driver updates, downloading selected updates and installing them with visible progress.

The project is written in **C# on .NET 10** and started at version **0.0.1**.

## Repository layout

```text
.github/workflows/       GitHub Actions build/release automation
docs/                    Architecture and technical documentation
src/
  Shared.Core/           Shared models, matching, OEM contracts, validation and update client
  App1.Service/          Privileged Windows Service
  App2.Desktop/          WPF main window + system tray icon
  App3.Setup/            Single installer/updater/uninstaller bootstrapper
tests/
  Shared.Core.Tests/     Unit tests
```

## Why the tray icon and Windows UI are one app

The tray icon does not need its own executable. App2.Desktop is a WPF application and also owns a WinForms `NotifyIcon`. Closing the main window hides it to the Windows notification area; the application can be reopened from the tray.

The Windows Service is intentionally separate because Windows services run outside the interactive desktop session and are the correct place for privileged driver operations.

The installer/updater is also separate because neither the service nor the desktop application can reliably replace their own running executable during an update.

## Current implementation

- dashboard with device, attention, update and service-health counters;
- one-click complete check;
- inventories Plug and Play devices through WMI;
- retains device hardware IDs and compatible IDs;
- correlates installed signed-driver metadata;
- classifies inventory rows as `OK`, `Driver mancante` or `Errore Windows`;
- queries Windows Update Agent for available driver updates;
- reads WUA driver provider, manufacturer, model, class, hardware ID and version date;
- correlates WUA updates to local devices by exact hardware/compatible-ID match;
- shows `Aggiornamento disponibile` on the device only when that correlation succeeds;
- shows the matching update metadata directly in the device details;
- lets the user select which Windows Update driver packages to install;
- reports real asynchronous Windows Update download/install progress to the UI;
- reports when a reboot is required and never forces a reboot;
- includes an extensible `IOemDriverProvider` layer;
- detects Intel, NVIDIA and AMD hardware from vendor IDs and metadata;
- detects Intel Driver & Support Assistant, NVIDIA App and AMD Software installation/version;
- shows a per-device recommended source: exact-match Windows Update first, then Intel/NVIDIA/AMD, otherwise Windows / OEM;
- exposes an OEM Providers tab and hands devices off to official vendor flows rather than scraping or invoking undocumented APIs;
- runs as a tray application;
- shows service version, start time and recent diagnostic logs;
- checks GitHub Releases for application updates;
- verifies the downloaded updater against GitHub's published SHA-256 digest before execution;
- keeps a last-known-good installation snapshot and automatically attempts rollback if an update fails;
- registers a normal Windows uninstall entry;
- installs the Windows Service and configures the tray application to start at sign-in;
- protects privileged IPC with a Windows named-pipe ACL, installed-client process verification and update-ID validation;
- writes persistent service logs under `%ProgramData%\TheEasyWayForDrivers\Logs`.

See [docs/architecture.md](docs/architecture.md), [docs/driver-management.md](docs/driver-management.md), [docs/oem-providers.md](docs/oem-providers.md), [docs/security-and-logging.md](docs/security-and-logging.md) and [docs/release-update.md](docs/release-update.md).

## Release

Every push to `main` runs tests and, when the pipeline is green, creates a new GitHub Release. Versions start at `0.0.1` and increment the patch component from the latest published release.

Each release contains:

```text
TheEasyWayForDrivers-Setup.exe
TheEasyWayForDrivers-Setup.exe.sha256
```

The executable is self-contained for Windows x64, so the target computer does not need a separate .NET runtime installation.

The workflow also supports Authenticode signing when the repository secrets `CODE_SIGNING_PFX_BASE64` and `CODE_SIGNING_PASSWORD` are configured. Without those secrets, builds remain unsigned.

## Development

Requirements:

- Windows 10/11;
- .NET 10 SDK;
- a current Visual Studio version or another IDE with .NET 10/WPF support.

Build and test:

```powershell
dotnet restore TheEasyWayForDrivers.slnx
dotnet build TheEasyWayForDrivers.slnx
dotnet test TheEasyWayForDrivers.slnx
```

To produce the final installer locally, first publish App1.Service and App2.Desktop into a payload containing `Service` and `Desktop` folders, zip that payload, then pass the ZIP path as the MSBuild property `PayloadZip` when publishing App3.Setup. The GitHub Actions workflow is the canonical example.
