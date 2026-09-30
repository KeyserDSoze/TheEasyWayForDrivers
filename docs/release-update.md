# Release and update flow

## Versioning

The source started at version **0.0.1**.

On every push to `main`, GitHub Actions first checks the latest published GitHub Release. A green pipeline increments its patch component and creates the matching `v0.0.N` release.

Failed workflow runs do not consume a public version number because the next successful run derives its version from the latest published release, not from the workflow run number.

## Build

The release workflow:

1. restores and tests the solution on Windows;
2. publishes App1.Service self-contained for `win-x64`;
3. publishes App2.Desktop self-contained for `win-x64`;
4. places both publish outputs into one payload ZIP;
5. embeds that ZIP into App3.Setup;
6. publishes App3.Setup as a self-contained single-file executable;
7. creates a GitHub Release containing **TheEasyWayForDrivers-Setup.exe**.

No .NET runtime installation is required on the target PC.

## Update

App2.Desktop asks App1.Service to check `releases/latest`. When a newer version exists, the user can press the update button. The new setup executable is downloaded to a temporary path and started elevated in update mode.

The updater stops the service and desktop process, replaces files, recreates/starts the service if needed, and leaves the new installation in `%ProgramFiles%\TheEasyWayForDrivers`.

File replacement is always delegated to the setup/updater executable.
