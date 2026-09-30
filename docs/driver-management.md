# Driver management

## Inventory

A scan correlates Plug and Play devices with signed driver metadata.

A device is marked as needing attention when Windows reports a non-zero \`ConfigManagerErrorCode\`. A device without a matching signed driver record is marked as not having an installed driver.

## Available updates

The first implementation asks Windows Update Agent for updates satisfying:

\`IsInstalled=0 and Type='Driver'\`

The UI lets the user select which updates to install. Installation results include whether Windows reports that a reboot is required.

## Progress

The IPC contract supports percentage progress from 0 to 100. The first WUA implementation reports meaningful operation phases (search, download, install, complete). A later increment should bind the native asynchronous WUA progress callbacks to expose continuous byte/update-level progress.

## Safety rules

- Never install an update that the user did not select.
- Prefer Windows Update or a verified OEM source.
- Keep the service privileged and the UI unprivileged.
- Validate hashes/signatures before adding non-Windows-Update download providers.
- Never silently reboot the machine.
