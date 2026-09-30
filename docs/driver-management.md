# Driver management

## Inventory

A scan correlates Plug and Play devices with signed driver metadata.

A device is marked as needing attention when Windows reports a non-zero
`ConfigManagerErrorCode`. A device without a matching signed driver record is
marked as not having an installed driver.

## Available updates

The first provider asks Windows Update Agent for updates satisfying:

`IsInstalled=0 and Type='Driver'`

The UI lets the user select which updates to install. Installation results
include whether Windows reports that a reboot is required.

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
- Keep the service privileged and the UI unprivileged.
- Reject malformed or oversized IPC install requests.
- Keep network identities out of the privileged named pipe.
- Validate hashes/signatures before adding non-Windows-Update download providers.
- Never silently reboot the machine.
