# Driver management

## Inventory

A scan correlates Plug and Play devices with signed driver metadata.

For every `Win32_PnPEntity`, the inventory now retains:

- the device instance ID;
- `HardwareID[]`;
- `CompatibleID[]`;
- manufacturer and class;
- Windows configuration error code.

Signed-driver metadata is read from `Win32_PnPSignedDriver`.

A device is marked as needing attention when Windows reports a non-zero
`ConfigManagerErrorCode`. A device without a matching signed driver record is
marked as not having an installed driver.

## Available updates

The Windows Update provider asks Windows Update Agent for updates satisfying:

`IsInstalled=0 and Type='Driver'`

For each returned driver update, the service reads the driver-specific WUA
metadata exposed by `IWindowsDriverUpdate`, including:

- driver class;
- hardware or compatible ID;
- manufacturer;
- model;
- provider;
- driver version date.

The UI lets the user select which updates to install. Installation results
include whether Windows reports that a reboot is required.

## Device-to-update correlation

The desktop correlates an available WUA driver package to a local device only
when the update's `DriverHardwareID` exactly matches one of the device's
`HardwareID` or `CompatibleID` values, using case-insensitive comparison.

No matching is inferred from the display name, vendor string or update title.
This deliberately avoids showing a false "update available" state.

When multiple WUA packages match the same device, the UI keeps all matches and
shows the package with the newest driver date as the preferred match.

The device row can therefore show these states:

- `OK`;
- `Driver mancante`;
- `Errore Windows`;
- `Aggiornamento disponibile`.

## Progress

Driver download and installation use the asynchronous Windows Update Agent
operations. While each operation is running, the service polls the WUA job
progress and forwards its real `PercentComplete` value to the desktop UI.

The overall progress bar reserves these ranges:

- discovery/preparation: 0-15%;
- driver download: 15-55%;
- driver installation: 60-95%;
- completion/result: 100%.

Cancellation requests ask the WUA job to abort before the service propagates
cancellation.

## Safety rules

- Never install an update that the user did not select.
- Prefer Windows Update or a verified OEM source.
- Correlate device/update identity from hardware IDs rather than display names.
- Keep the service privileged and the UI unprivileged.
- Reject malformed or oversized IPC install requests.
- Keep network identities out of the privileged named pipe.
- Validate hashes/signatures before adding non-Windows-Update download providers.
- Never silently reboot the machine.
