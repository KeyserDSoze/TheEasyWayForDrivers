# TheEasyWayForDrivers

TheEasyWayForDrivers is a Windows utility for checking installed hardware drivers, identifying devices that need attention, finding driver updates, downloading selected updates and installing them with visible progress.

The project is written in **C# on .NET 10** and starts at version **0.0.1**.

## Repository layout

```text
.github/workflows/       GitHub Actions build/release automation
docs/                    Architecture and technical documentation
src/
  Shared.Core/           Shared models, contracts and release update client
  App1.Service/          Privileged Windows Service
  App2.Desktop/          WPF main window + system tray icon
  App3.Setup/            Single installer/updater bootstrapper
tests/
  Shared.Core.Tests/     Unit tests
```

## Why the tray icon and Windows UI are one app

The tray icon does not need its own executable. App2.Desktop is a WPF application and also owns a WinForms `NotifyIcon`. Closing the main window hides it to the Windows notification area; the application can be reopened from the tray.

The Windows Service is intentionally separate because Windows services run outside the interactive desktop session and are the correct place for privileged driver operations.

The installer/updater is also separate because neither the service nor the desktop application can reliably replace their own running executable during an update.

## Current first implementation

- inventories Plug and Play devices through WMI;
- correlates installed signed-driver metadata;
- flags devices with Windows configuration errors or no signed driver record;
- queries Windows Update Agent for available driver updates;
- lets the user select which driver updates to install;
- reports operation progress to the UI;
- reports when a reboot is required and never forces a reboot;
- runs as a tray application;
- checks GitHub Releases for application updates;
- downloads and starts the single updater executable;
- installs the Windows Service and configures the tray application to start at sign-in.

See [docs/architecture.md](docs/architecture.md), [docs/driver-management.md](docs/driver-management.md) and [docs/release-update.md](docs/release-update.md).

## Release

Every push to `main` runs tests and creates a new GitHub Release. The release version is `0.0.N`, using the GitHub Actions run number, and contains one self-contained x64 installer:

```text
TheEasyWayForDrivers-Setup.exe
```

The target computer does not need a separate .NET runtime installation.

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
