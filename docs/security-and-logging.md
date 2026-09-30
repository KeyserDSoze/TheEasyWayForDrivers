# Service security and logging

## Named pipe

The desktop application and the privileged Windows Service communicate over
`TheEasyWayForDrivers.Service.v1`.

The pipe has an explicit Windows ACL:

- Local System: full control;
- local Administrators: full control;
- interactive Windows sessions: read/write;
- identities carrying the Windows Network SID: denied.

The desktop connects with identification-level impersonation so the service can
record the caller identity in its audit log.

The service still exposes only a small fixed command set. Driver installation
requests are further constrained to a maximum of 128 Windows Update IDs and
every ID must be a valid GUID.

This is defense in depth. A future production hardening step can add a
per-installation authentication secret or signed desktop-client attestation.

## Service logs

The Windows Service retains its normal Microsoft.Extensions.Logging providers
and adds a daily text log under:

`%ProgramData%\TheEasyWayForDrivers\Logs`

Files use the form `service-YYYYMMDD.log` and files older than 14 days are
removed when the service starts.

The log includes:

- driver-update searches;
- number of available updates;
- IPC command and caller identity;
- installation result code;
- reboot-required state;
- failures and exceptions.

Do not write secrets, driver payloads or arbitrary request bodies to the log.
