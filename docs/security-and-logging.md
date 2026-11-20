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


## Setup staging: local filesystem threat model

The installer stages files before privileged setup operations. Treat the staging
directory and **every ancestor directory** as a trust boundary: an attacker who
can rename or replace an ancestor with a junction, symbolic link, or other
reparse point may redirect a later path-based write outside the intended root.

### What the current mitigations do and do not guarantee

- Rejecting reparse points before directory creation and checking again
  afterward prevents traversal through links that are present at those checks.
- Creating a single unpredictable temporary subdirectory reduces exposure to
  predictable staging names, but randomness is not an access-control boundary.
- Neither technique makes the check and subsequent file open atomic. A
  concurrent process with write/rename rights on an ancestor can potentially
  replace it between the validation and the write (TOCTOU).
- These mitigations must **not** be described as a complete defense against
  a local adversary who can mutate the staging tree concurrently.

### Requirements before claiming race-resistant staging

1. Use a staging root whose owner and DACL prevent untrusted users and
   processes from modifying or renaming the root or its ancestors. Verify
   effective permissions and the owner on Windows before elevation.
2. Anchor directory traversal and file creation to validated Windows handles;
   check reparse-point flags and stable directory identity. Do not rely solely
   on a second path-string check after opening a destination.
3. Fail closed when permissions, identity, or handle-based validation cannot
   be established; do not silently fall back to an untrusted shared Temp path.
4. Exercise real Windows tests with concurrent ancestor swaps, junctions,
   symbolic links, ordinary installation, and failure/cleanup paths. Assert
   that no write escapes the approved staging root.
5. Require successful Windows CI on the exact change SHA and a Windows
   environment verification before closing the hardening work.

This is a **remaining hardening requirement**, not evidence that exploitation
has occurred. It is tracked in issue #529, separately from the completed
pre-existing-junction mitigation in #528. It does not authorize executing
untrusted OEM driver packages or bypassing Windows driver signature checks.

## Installer staging: Windows ancestor handle pins (#562)

The installer pins all existing ancestor directories from the volume root to
its temporary staging directory using Win32 handles without DELETE sharing.
Each handle is verified as a directory rather than a reparse point. Because
Windows CI showed directory handles alone did not prevent Directory.Move,
the guard also holds a random exclusive delete-on-close file inside the leaf.
During
ZIP extraction, the immediate file parent is also pinned while writing.
See [setup-anchored-staging-562.md](setup-anchored-staging-562.md).

The extra leaf lock is intended to block rename while active; the first
handle-only version did **not** block it in Windows CI. It does not make nested directory creation or path-based file opens
atomic, and it does not establish a private, owner-verified staging ACL.
The remaining TOCTOU and Windows QA work stays open in #529/#85.


## Private installer staging root ACL (#563)

On Windows, the installer creates the unpredictable staging child directly
under Temp using `CreateDirectoryW` with a `SECURITY_ATTRIBUTES` security
descriptor. The DACL is protected from parent inheritance (`D:P`) and
grants file/directory full control only to the current Windows token SID,
Local System and BUILTIN Administrators. It is applied **at creation**, not
after an initial insecure `Directory.CreateDirectory` call.

After creation and before privileged setup mutations, the installer reads
back the actual Windows security descriptor, checks the owner and exact
allowlist of full-control ACEs, and fails closed when the descriptor is
missing or unexpected. The normal ZIP/PE validation is unchanged.

This is not an atomic handle-relative write protocol; a privileged process
with matching ACL rights can still race a path-based open. Directory creation
and swap-concurrency Windows QA remain in #529 / #85. These ACLs do not sign
the installer or any third-party driver (#283).


## Exact staging DACL principals (#564)

The Windows staging DACL validation requires one and only one explicit
full-control allow ACE for each of the current token SID, LocalSystem and
BUILTIN Administrators. Three ACEs merely drawn from this allowlist are
insufficient: duplicates cannot stand in for a missing principal.
Each ACE must have exactly the object/container inheritance flags; inherited,
inherit-only, no-propagate, deny and callback ACEs are rejected. Windows
regression tests alter the DACL of isolated temporary staging directories
and verify that duplicates and extra ACE flags fail closed. This does not
provide atomic handle-relative I/O or fully resolve #529.

## Stepwise pinned ZIP directory traversal (#565)

Each missing subdirectory beneath the staging root is created one component
at a time, holding a `SetupDirectoryAnchor` on its parent before mkdir.
After a reparse check, the new child is also anchored. All guards stay open
until the ZIP directory entry finishes or the file entry's bytes have been
copied; they are disposed in reverse order on success or failure. Explicit
and implicit ZIP directories use the same logic, with `FileMode.CreateNew`
for files. Regression tests cover nested implicit directories and failure
cleanup without residual temporary lock files.

This is **not** atomic handle-relative file creation: a child can still be
swapped between mkdir and its anchor acquisition. Further concurrency
hardening remains in #529; real Windows QA remains in #85.


## Windows first payload file: handle-relative create (#566)

`SetupStagingDirectory.CreatePayloadFile` now opens a pinned staging
directory and creates only the single child `payload.zip` via
`NtCreateFile` with `OBJECT_ATTRIBUTES.RootDirectory` referring
to the already-open directory handle. `FILE_CREATE` refuses existing
files, links and directories; `FILE_OPEN_REPARSE_POINT` /
`OBJ_DONT_REPARSE` and single-component name validation reject
redirection and path traversal. There is no fallback to a path-based
`File.Create` when Windows' native call fails. The returned
file handle is passed to a `FileStream` and owns the
subsequent writes, even after the parent guard is disposed.

This is *only* the initial embedded ZIP staging write. Full
handle-relative directory creation and ZIP extraction remain outside
this increment; concurrency tests and installed Windows 11 QA are
still required by #529/#85. On non-Windows test hosts the previous
`FileMode.CreateNew` behavior is preserved.


## Embedded ZIP file entries created via pinned parent handles (#567)

For each file inside the embedded Service/Desktop ZIP, Windows now uses
`WindowsHandleRelativeFile.CreateNew` with `NtCreateFile` and
the verified *leaf directory handle* from the stepwise anchor chain.
The only name passed to the native call is one filename component
(`Path.GetFileName(target)`), rather than the whole temporary
path. Exclusive `FILE_CREATE` prevents replacing an existing file,
hardlink or symlink. On Windows, failures abort extraction with no
path-based fallback. On non-Windows test hosts, extraction continues to
use `FileMode.CreateNew`. All ZIP preflight, size, cancellation
and mandatory component checks remain unchanged.

Windows regression coverage includes a preexisting file symlink that
must not alter its outside target, nested valid extractions and cleanup
of temporary directory lock files on success and failure.

This hardens **file creation**, but the stepwise creation of subdirectories
is still path-based and can race between mkdir and handle acquisition.
It does not prove concurrency safety, Windows 11 installation QA,
or completion of #529/#85.


## Native handle-relative ZIP subdirectories (#568)

On Windows, each Service/Desktop ZIP directory component is now created
or opened via `NtCreateFile(FILE_OPEN_IF | FILE_DIRECTORY_FILE)`,
relative to the already pinned parent handle. `OBJ_DONT_REPARSE` and
`FILE_OPEN_REPARSE_POINT` reject redirected lookup; the returned
handle is checked for directory and non-reparse attributes. An exclusive
delete-on-close rename-fence file is also created **relative to that same
child handle** before continuing to the next component. The entire chain
of child handles and locks remains live until the entry is fully written,
with reverse-order cleanup on every error path. The file contents already
use relative NT creation (#567); the existing ZIP preflight and quotas
are unchanged. No path-based fallback is used for Windows ZIP children.

Windows regression coverage includes nested explicit/implicit directory
entries, file-as-parent failure, existing junction/symlink rejection,
successful extraction, and lock cleanup after errors.

The staging **root** is still initially located using its path, and other
installer operations (including destination install/rollback and cleanup)
have their own trust boundaries. Native handle-relative ZIP writes alone
do not prove end-to-end TOCTOU safety. Real swap-concurrency validation and
Windows 11 install/rollback QA remain in #529/#85. Signing remains #283.


## Anchor the root staging lock to the verified directory handle (#569)

The original root `SetupDirectoryAnchor.Open` used
`new FileStream(Path.Combine(fullPath, randomLockName))` *after*
verifying the leaf directory handle. A privileged concurrent rename could
make that fresh pathname lookup designate a different directory.

On Windows the root now creates its exclusive delete-on-close rename lock
with `WindowsHandleRelativeFile.CreateDeleteOnCloseLock` and
`OBJECT_ATTRIBUTES.RootDirectory` pointing to the verified
leaf handle. The temporary lock is released before the parent handles,
even on failures. No fallback pathname lookup is made to create this
lock. Synthetic Windows tests attempt concurrent renames while the root
is pinned and confirm that renames succeed only after Dispose; a
symlinked staging root cannot cause a lock in the external target.

**Scope:** the fix removes a path-based second lookup for the root
rename lock. The *initial* ancestor and leaf handles are still opened
by `CreateFileW(fullPath)`, and this does not prove
all concurrent swap possibilities impossible or replace real installed
Windows 11 upgrade/rollback validation (#529, #85).


## Deterministic PnPUtil INF detection under Windows CI load (#570)

A Windows CI run for #569 reproduced a `RegexMatchTimeoutException`
in the otherwise harmless Italian PnPUtil evidence test: the
`VisibleInfFileName` regex could exceed its 500ms deadline under
runner CPU contention. Driver Store evidence classification now uses a
bounded, deterministic ASCII token scan after whole-line privacy
redaction, instead of a timed regex for INF filenames. It preserves the
previous isolation boundaries around `*.inf` basenames,
rejects path-embedded and overlong names, and never interprets localized
rank values as updates. Other privacy checks and limits remain in place.

Regression tests cover benign localized output, path/suffix/identifier
boundaries, and long tokens. This is a stability/privacy fix; it is not
a new OEM catalogue or a claim of hardware update coverage (#91, #132).


## Staging ancestors: handle-relative traversal from the volume root (#571)

On Windows, SetupDirectoryAnchor.Open now opens the LOCAL VOLUME ROOT
once with CreateFileW, then walks all existing descendants one component
at a time using NtCreateFile(FILE_OPEN | FILE_DIRECTORY_FILE |
FILE_OPEN_REPARSE_POINT) with the previously verified directory handle
as OBJECT_ATTRIBUTES.RootDirectory. Each returned handle must identify
a directory without reparse attributes before the next component is
opened. Missing components fail closed without being created; no
absolute-path fallback is used for descendants. Intermediate handles
only request read-attributes/traverse access, and the final staging
directory retains the child-creation permission needed for its
delete-on-close rename lock (#569).

Windows regression tests cover deep existing paths with spaces, a
nonexistent intermediate ancestor, an already-existing symlink to a
fully populated outside path, UNC roots, and cleanup of leaf locks.
Non-Windows test behavior is unchanged.

This reduces TOCTOU exposure from repeated full-path ancestor lookups,
but opening the local volume root still uses a named Win32 handle.
Directory handles do not veto all possible rename operations on every
filesystem (#562), and no CI test proves comprehensive immunity to
same-privilege adversarial swaps or Windows 11 install/update/rollback.
The broader security/QA tasks remain in #529 and #85.


## Verify private staging ACL on the actual directory handle (#572)

The Windows staging ACL readback no longer performs a second
GetFileSecurityW(fullPath) lookup after anchoring. The final
SetupDirectoryAnchor leaf requests READ_CONTROL, and
WindowsPrivateStagingDirectory.Verify reads owner and DACL with
GetKernelObjectSecurity(SafeFileHandle) on that exact verified object.
It still requires a protected DACL and precisely one allowed full-control
ACE for each distinct principal (current token SID, SYSTEM,
Administrators), with a permitted owner. Unexpected descriptors fail
closed without echoing paths or SIDs.

SetupEngine now passes the same long-lived SetupDirectoryAnchor directly
to the verification routine. The existing path-taking public API first
opens its own short-lived anchor and verifies THAT handle, avoiding a
separate path-based security-descriptor lookup.

Windows tests verify a valid private root, a changed DACL on a still-open
anchor, an already-disposed anchor, and no leaked lock files. These are
point-in-time ACL checks. They do not replace deterministic same-privilege
swap tests or installed Windows 11 upgrade/rollback QA (#529/#85).


## Deterministic traversal swap tests and post-fence identity check (#573)

On Windows, `SetupDirectoryAnchor.Open` now reopens each existing
parent/child link **relative to its previously verified parent handle**
after creating the leaf's native delete-on-close rename fence.
`GetFileInformationByHandle` compares the original and reopened
volume serial number and file index. Any different identity, reparse
point, missing name or native error aborts anchoring; disposal removes
the fence from the original directory. These checks never reopen
absolute descendant paths and never create missing directories.

A test-only internal validation observer synchronizes race injection
at an exact boundary, without affecting normal production callers.
Windows regression tests replace an as-yet-unopened child with a
symlink to an outside canary, then attempt to move and substitute a
**verified leaf before its lock is created**. If Windows vetoes the
move, the original anchor remains valid; if the move succeeds, the
returned anchor must fail closed and both locations must have no
residual lock. The external canary must remain unchanged.

This is an additional *point-in-time* identity validation, not a
global lock over every ancestor. Subsequent same-privilege swaps,
other installer path operations and real installation/rollback tests
remain to be evaluated under #529 and #85.


## Handle-relative reopen and cleanup of the embedded ZIP (#574)

After staging the embedded ZIP, elevated setup no longer uses
`File.OpenRead(zipPath)` and `File.Delete(zipPath)` on
Windows. It reopens only `payload.zip` through the long-lived
verified staging root handle with native `NtCreateFile(FILE_OPEN)`
and exclusive read access. The opened file's real handle attributes
are checked for directory/reparse status before requesting
`SetFileInformationByHandle(FileDispositionInfo)`. The
verified object is deleted when that same read stream closes, whether
extraction succeeds or fails. A symlink or a missing file causes
failure without following an outside target or deleting it.

Non-Windows test hosts retain `File.OpenRead/File.Delete`
semantics. This removes two pathname re-resolutions during embedded
ZIP extraction; it does not guarantee file identity between the
earlier write handle's disposal and the new exclusive read, nor
validate all installed destinations. Wider TOCTOU and actual
installation/rollback QA remain in #529 and #85.
