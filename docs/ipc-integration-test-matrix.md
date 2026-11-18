# Service IPC integration test matrix — first automation slice (#85)

This file distinguishes **mock-backed command integration**, **transport security**, and
**real Windows validation**. A green CI result does not mean driver installation or
OEM discovery works on every PC.

## Automated on Windows CI

`tests/App1.Service.Tests/IpcCommandDispatcherTests.cs` exercises the **actual service
command dispatcher**, with fake WMI/WUA/OEM providers. The separate
`NamedPipeServerWindowsTransportTests.cs` connects to a **real Windows named
pipe with the production ACL and ClientProcessVerifier** from an uninstalled
xUnit/testhost process. It asserts a denial before any command is read,
including on the second connection; no fake/disabled authorization is used.
A denied testhost connection is NOT evidence that an authorized installed
Desktop client works, nor that a service account or all pipe ACL scenarios
have been validated.

| Desktop IPC command | Automated coverage | Real Windows follow-up |
| --- | --- | --- |
| `scan` | Fake inventory returns a typed device | WMI, drivers with errors/missing device |
| `search-updates` | Recommended/Comprehensive routing, progress, invalid mode | Windows Update Agent, offline, policies, no results |
| `install-updates` | Update-ID validation, deduplication and fake install callback only | Consented VM/snapshot installation and reboot |
| `oem-providers` | Desktop JSON request shape, typed OEM response, fault isolation | Actual Intel/AMD/NVIDIA/system OEM status in UI |
| `check-app-update` | Null release response stays an empty JSON object | Real release check and signed update |
| `diagnostics` | Response shape and process diagnostics, without driver access | Installed service logs and permissions |
| Unknown / cancelled | Explicit rejection / early cancellation; real pipe rejects uninstalled testhost twice | Authorized Desktop client, real service identity, post-auth protocol edge cases |

`tools/Test-IpcCommandContract.ps1` also blocks PRs and releases if the Desktop
declares a command absent from the dispatcher or the reviewed command inventory
changes unexpectedly.

## Uncovered acceptance criteria (release hardening backlog)

- [x] Windows CI negative real named-pipe smoke: testhost is denied by
  the unmodified PID/executable-path verifier and production pipe ACL twice,
  before it sends any request; test shuts down the listener on cancellation.
- [ ] Windows named-pipe test from the **installed, authorized** Desktop,
  including malformed/oversized/stalled requests AFTER authorization; real
  service-account context, denial from a separate unauthorized process,
  and full error/reconnect protocol remain to be validated.
- [ ] Installer/update/rollback/uninstaller VM tests with recorded artifact hashes,
  service process versions, tray behavior and clean/reboot states.
- [ ] WPF UI automation with screen verification (OEM tab, Discovery+, search, filters,
  diagnostics, empty/error states, cancellation), with a mock driver service.
- [ ] Real Windows Update search on various OS policies, networking states and PnP
  hardware profiles. No unsolicited driver installations.
- [ ] Real OEM coverage on physical test machines (including unrecognized vendors),
  catalog source failures and stale/missing metadata.
- [ ] Store repeatable results as CI/test artifacts and mark QA Windows #5 separately.

See also [runtime-validation.md](runtime-validation.md), issue #85 and QA issue #5.

## Incremental Windows transport smoke — #551

Unlike dispatcher-only mock tests, `NamedPipeServerWindowsTransportTests`
constructs `NamedPipeServer` with the actual Windows `NamedPipeServerStreamAcl`
constructor and invokes `ClientProcessVerifier` against the current testhost
PID. Since testhost is not the installed App2.Desktop.exe, the expected
response is the **generic, structured authorization error**, with no data
or installation command. A second connection must receive the same error.
The cancellation token closes the listener before the test exits; no
driver action or mock authorization bypass is involved.
