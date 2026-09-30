# Windows 11 runtime validation

CI can compile, test and package OmegaDrive, but it cannot prove that a real
Windows installation exposes the expected WMI, Windows Update, service, tray
and setup behavior. The release therefore includes an executable validation
script for a physical or virtual Windows 11 machine.

## Basic validation

Install the current OmegaDrive release and run PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\Validate-OmegaDriveRuntime.ps1
```

The basic run checks:

- Windows 11 build and x64 architecture;
- service, desktop and updater files;
- Windows Service state, auto-start mode and configured binary path;
- WMI `Win32_PnPEntity` access;
- creation of the Windows Update Agent `Microsoft.Update.Session` COM object;
- OmegaDrive per-user settings;
- current-user startup preference;
- Authenticode status of service, desktop and updater;
- whether a rollback snapshot currently exists.

Unsigned builds produce warnings rather than failing the smoke test. An invalid
or broken signature is treated as a critical failure.

## Deep Windows Update validation

To make Windows Update perform a real driver search:

```powershell
powershell -ExecutionPolicy Bypass -File .\Validate-OmegaDriveRuntime.ps1 -Deep
```

This may take time and depends on Windows Update policy, network connectivity
and the state of the test machine.

## JSON report

For repeatable test evidence:

```powershell
powershell -ExecutionPolicy Bypass -File .\Validate-OmegaDriveRuntime.ps1 -Deep -JsonOutputPath .\OmegaDrive-runtime-report.json
```

The script exits with code 1 when a critical check fails, so it can also be
called from a VM automation or local test harness.

## Manual checks still required

The script cannot replace these interactive checks:

- installer elevation/UAC flow;
- first-run window and settings persistence;
- tray icon, minimize/close behavior and notifications;
- visible Windows Update progress;
- selecting and installing a real driver package;
- reboot-required UI;
- update rollback after an intentionally induced failure;
- uninstall cleanup;
- SmartScreen behavior for a file downloaded from the public release URL.
