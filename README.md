# OmegaDrive Driver Manager

OmegaDrive Driver Manager is a Windows utility for checking installed hardware drivers, identifying devices that need attention, finding driver updates, downloading selected updates and installing them with visible progress.

The project is written in **C# on .NET 10**. The repository and selected compatibility identifiers still retain the original TheEasyWayForDrivers name during the staged OmegaDrive migration.

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
- searchable/filterable driver inventory with status/source filters, visible-result count and attention-first sorting;
- searchable/filterable Windows Update list with selected/unselected and result-type filters, plus explicit search-source and result-kind columns;
- advanced/hidden search results are never selected by default;
- colored status badges for OK, available updates, missing drivers and Windows errors;
- one-click complete check;
- inventories Plug and Play devices through WMI;
- retains device hardware IDs and compatible IDs;
- correlates installed signed-driver metadata;
- classifies inventory rows as `OK`, `Driver mancante` or `Errore Windows`;
- offers two driver-search modes: **Recommended** and **Comprehensive**;
- Recommended search uses the configured Windows update service, online, for non-hidden applicable drivers;
- Comprehensive search adds a direct Windows Update pass plus advanced/hidden/potentially superseded candidates, deduplicated by update ID;
- resolves driver download size from WUA `MaxDownloadSize`, falls back to `MinDownloadSize`, and displays `N/D` instead of a misleading zero when size metadata is unavailable;
- reads WUA driver provider, manufacturer, model, class, hardware ID and version date;
- correlates WUA updates to local devices by exact hardware/compatible-ID match;
- shows `Aggiornamento disponibile` on the device only when that correlation succeeds;
- shows the matching update metadata directly in the device details;
- lets the user select which Windows Update driver packages to install;
- supports one-device installation from the selected device details, with explicit confirmation before starting;
- reports real asynchronous Windows Update download/install progress to the UI;
- reports when a reboot is required and never forces a reboot;
- includes an extensible `IOemDriverProvider` layer;
- detects Intel, NVIDIA and AMD hardware from vendor IDs and metadata;
- detects Intel Driver & Support Assistant, NVIDIA App and AMD Software installation/version;
- detects the PC manufacturer/model and recognizes Dell, Lenovo, HP, ASUS, Acer and Microsoft Surface;
- shows a per-device recommended source: exact-match Windows Update first, then system-OEM-aware Intel/NVIDIA/AMD routing, otherwise Windows / OEM;
- exposes an OEM Providers tab for chip vendors and the PC manufacturer, with official support handoff instead of scraping or undocumented APIs;
- uses the OmegaDrive brand and shield/road icon consistently for the desktop executable, WPF window, tray icon and setup executable;
- includes a branded graphical installer/updater/uninstaller instead of a console-only setup;
- includes a first-run onboarding flow with startup, notification and automatic-check choices;
- includes persistent per-user settings for Windows startup, minimize-to-tray, close-to-tray and tray notifications;
- runs as a tray application and follows the configured minimize/close behavior;
- shows deduplicated tray notifications for newly changed driver-update sets and newly available application versions;
- shows service version, start time and recent diagnostic logs;
- includes an About page with app/runtime/architecture information, executable path and embedded Authenticode-signature presence;
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
OmegaDrive-Setup.exe
OmegaDrive-Setup.exe.sha256
TheEasyWayForDrivers-Setup.exe          # temporary migration alias
TheEasyWayForDrivers-Setup.exe.sha256
OmegaDrive-build-manifest.json
Validate-OmegaDriveRuntime.ps1
```

The executable is self-contained for Windows x64, so the target computer does not need a separate .NET runtime installation.

The workflow also supports Authenticode signing when the repository secrets `CODE_SIGNING_PFX_BASE64` and `CODE_SIGNING_PASSWORD` are configured. When signing is enabled, CI now verifies service, desktop and setup with SignTool using the Authenticode policy before the release is created. Without those secrets, builds remain unsigned and the release manifest records that state explicitly.

Each release also contains a JSON build manifest with SHA-256 hashes, file versions and signature status for service, desktop and setup binaries.

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


## Windows 11 runtime validation

After installing OmegaDrive on a Windows 11 test machine, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Validate-OmegaDriveRuntime.ps1
```

For a real Windows Update driver search as part of the validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\Validate-OmegaDriveRuntime.ps1 -Deep -JsonOutputPath .\OmegaDrive-runtime-report.json
```

The script checks the installed binaries, service registration/state, service path,
WMI Plug and Play access, Windows Update Agent COM availability, settings,
startup preference, Authenticode status and rollback snapshot state.
