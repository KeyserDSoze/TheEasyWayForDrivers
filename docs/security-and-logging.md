# Service security and logging

## Named pipe

The desktop application and the privileged Windows Service communicate over
`TheEasyWayForDrivers.Service.v1`.

The pipe has an explicit Windows ACL:

- Local System: full control;
- local Administrators: full control;
- interactive Windows sessions: read/write;
- identities carrying the Windows Network SID: denied.

ACL access is only the first gate. After a client connects, the service asks
Windows for the named-pipe client process ID and resolves the process
executable. The request is rejected unless the caller is the installed:

`%ProgramFiles%\TheEasyWayForDrivers\Desktop\App2.Desktop.exe`

This means an arbitrary process in an interactive local session cannot invoke
the privileged service merely because it can open the pipe. A local
administrator can still replace files under Program Files; that is already an
administrator-equivalent trust boundary.

The desktop connects with identification-level impersonation so the service can
also record the caller identity in its audit log.

The service exposes only a small fixed command set. Driver installation
requests are constrained to a maximum of 128 Windows Update IDs and every ID
must be a valid GUID.

## Update integrity

GitHub release metadata exposes a SHA-256 digest for the setup asset. The
service returns that digest with update metadata. After download, the desktop
computes the SHA-256 of the actual file and refuses to execute it if the values
do not match.

This protects against corrupted or unexpectedly modified downloads. It is not a
replacement for publisher identity verification. Authenticode code signing is
therefore supported by the release workflow when a signing certificate is
configured through repository secrets.

## Service logs

The Windows Service retains its normal Microsoft.Extensions.Logging providers
and adds a daily text log under:

`%ProgramData%\TheEasyWayForDrivers\Logs`

Files use the form `service-YYYYMMDD.log` and files older than 14 days are
removed when the service starts.

The log includes:

- driver-update searches;
- number of available updates;
- IPC command, caller identity and client process;
- rejected client processes;
- installation result code;
- reboot-required state;
- failures and exceptions.

Do not write secrets, driver payloads or arbitrary request bodies to the log.
