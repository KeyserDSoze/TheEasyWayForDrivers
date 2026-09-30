# Release and update flow

## Versioning

The source starts at version **0.0.1**.

On every push to \`main\`, GitHub Actions uses the workflow run number to produce \`0.0.N\` and creates a matching \`v0.0.N\` release.

## Build

The release workflow:

1. restores and tests the solution on Windows;
2. publishes App1.Service self-contained for \`win-x64\`;
3. publishes App2.Desktop self-contained for \`win-x64\`;
4. places both publish outputs into one payload ZIP;
5. embeds that ZIP into App3.Setup;
6. publishes App3.Setup as a self-contained single-file executable;
7. creates a GitHub Release containing **TheEasyWayForDrivers-Setup.exe**.

No .NET runtime installation is required on the target PC.

## Update

App2.Desktop checks \`releases/latest\`. When a newer version exists, the user can press the update button. The new setup executable is downloaded to a temporary path and started elevated in update mode.

The updater stops the service and desktop process, replaces files, recreates/starts the service if needed, and leaves the new installation in \`%ProgramFiles%\\TheEasyWayForDrivers\`.

The service can also perform the release check through the shared update provider; file replacement is always delegated to the setup/updater executable.
