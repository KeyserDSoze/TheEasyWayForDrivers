# Service IPC integration test matrix — first automation slice (#85)

This file distinguishes **mock-backed command integration**, **transport security**, and
**real Windows validation**. A green CI result does not mean driver installation or
OEM discovery works on every PC.

## Automated on Windows CI

`tests/App1.Service.Tests/IpcCommandDispatcherTests.cs` exercises the **actual service
command dispatcher**, with fake WMI/WUA/OEM providers. The authenticated Windows
named-pipe transport remains a separate security boundary and is **not** simulated
by bypassing or disabling authorization.

| Desktop IPC command | Automated coverage | Real Windows follow-up |
| --- | --- | --- |
| `scan` | Fake inventory returns a typed device | WMI, drivers with errors/missing device |
| `search-updates` | Recommended/Comprehensive routing, progress, invalid mode | Windows Update Agent, offline, policies, no results |
| `install-updates` | Update-ID validation, deduplication and fake install callback only | Consented VM/snapshot installation and reboot |
| `oem-providers` | Desktop JSON request shape, typed OEM response, fault isolation | Actual Intel/AMD/NVIDIA/system OEM status in UI |
| `check-app-update` | Null release response stays an empty JSON object | Real release check and signed update |
| `diagnostics` | Response shape and process diagnostics, without driver access | Installed service logs and permissions |
| Unknown / cancelled | Explicit rejection / early cancellation | Named-pipe client process verification and protocol edge cases |

`tools/Test-IpcCommandContract.ps1` also blocks PRs and releases if the Desktop
declares a command absent from the dispatcher or the reviewed command inventory
changes unexpectedly.

## Uncovered acceptance criteria (release hardening backlog)

- [ ] Genuine Windows named-pipe transport tests from an **installed, authorized**
  client process; unauthorized and malformed client requests must be rejected.
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
