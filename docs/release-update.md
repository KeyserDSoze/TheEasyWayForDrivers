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
4. optionally Authenticode-signs service and desktop when code-signing secrets are configured;
5. places both publish outputs into one payload ZIP;
6. embeds that ZIP into App3.Setup;
7. publishes App3.Setup as a self-contained single-file executable;
8. optionally Authenticode-signs the setup executable;
9. generates `TheEasyWayForDrivers-Setup.exe.sha256`;
10. creates a GitHub Release containing the setup executable and SHA-256 sidecar.

No .NET runtime installation is required on the target PC.

### Optional code signing

The workflow recognizes these repository secrets:

- `CODE_SIGNING_PFX_BASE64`: base64-encoded PFX containing the Authenticode certificate and private key;
- `CODE_SIGNING_PASSWORD`: PFX password.

When both are present, the workflow signs App1.Service.exe,
App2.Desktop.exe and TheEasyWayForDrivers-Setup.exe with SHA-256 and a trusted
timestamp. If the secrets are absent, the release remains unsigned.

The certificate itself must never be committed to the repository.

## Secure update

App2.Desktop asks App1.Service to check `releases/latest`. The service accepts
an update only when the setup asset has a valid GitHub `sha256:` digest.

When the user starts the update:

1. the setup executable is downloaded to a temporary path;
2. App2.Desktop computes its SHA-256;
3. the result must match the digest returned from GitHub release metadata;
4. only then is the setup executable started elevated in `--update` mode.

A digest mismatch deletes/rejects the downloaded file.

## Rollback

Before replacing an existing installation, App3.Setup stops the running
components and stores the current Service, Desktop and Updater directories
under:

`%ProgramData%\TheEasyWayForDrivers\Rollback`

If file replacement, service configuration or service startup throws an error,
the updater attempts to restore that last-known-good snapshot, recreates the
service configuration and restarts the previous desktop version.

The rollback snapshot is intentionally kept after a successful update so it is
available for diagnostics or a future explicit rollback feature.

## Uninstall

Setup registers TheEasyWayForDrivers under the normal Windows uninstall
registry location. The uninstall command invokes the installed bootstrapper
with:

`--uninstall`

Uninstall stops/removes the Windows Service, removes tray startup entries,
removes the uninstall registration, deletes application data and schedules the
installer directory for deletion after the running uninstaller exits.


## Preserving desktop startup preferences

Fresh installs enable the desktop tray application at Windows sign-in by default.

Application updates and rollback operations do not rewrite the per-user
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` preference. This is
intentional: once the user changes "Avvia con Windows" from the desktop
settings page, a later application update must not silently turn it back on.

Uninstall still removes the startup entry as part of cleanup.


## OmegaDrive setup asset migration

v0.0.12 introduces `OmegaDrive-Setup.exe` as the canonical setup filename.
The release also publishes the byte-identical legacy
`TheEasyWayForDrivers-Setup.exe` alias for update compatibility with older
installed clients.

The v0.0.12 update client prefers the OmegaDrive asset and falls back to the
legacy asset. After the installed base has migrated, the legacy alias can be
removed in a later release.
