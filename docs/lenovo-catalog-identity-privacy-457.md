# Lenovo.Client.Scripting catalog: driver-only identity and privacy (#457)

## Why

The optional `Find-LnvUpdate -MachineType ... -PackageType '2' -ListAll`
query describes a published Lenovo MachineType catalog. It does **not**
prove that an individual device needs a driver and it cannot authorize
download, installation, or replacement of a Windows driver.

The original shared catalog parser used one display normalizer for all
fields. That could coerce `PackageType="2\n"` into `"2"`, truncate
distinct package identifiers before deduplication, or expose strings
containing a local path, PnP instance ID or account data.

## Rules implemented in Core

- Only the exact JSON numeric value `2` or string `"2"`, optionally
  padded by ASCII spaces, is the official Driver package type.
  TAB/CR/LF, bidi controls, Unicode whitespace and lookalikes never
  become type 2 via normalization.
- Lenovo package identity `ID` is checked **in full** (1–160 ASCII
  characters: letters, digits, dot, underscore and dash); no truncation
  or control-character repair before case-insensitive deduplication.
- Display fields `Name`, `Category`, `Version`, `ReleaseDate`
  retain readable Unicode, suppress control/bidi/surrogates, and redact
  complete inputs containing local paths, URLs, emails or PnP/serial
  markers **before** applying the 160-character display limit.
- `DiscoveryResultAdapter.FromLenovo` and
  `OemPackageAssessment.FromLenovo` revalidate public `Package`
  records even when they arrive from old IPC data or direct callers.
- The existing 1000-received-record hard stop and 50-item
  category-balanced presentation preview remain unchanged.
  **An omitted catalog row does not prove that no driver exists.**

## Regression and release QA

`LenovoCatalogIdentityPrivacyTests` models exact driver-type boundaries,
invalid and 161-character package IDs, redaction after the preview cap,
direct-call bypass, Unicode format controls and 800 Audio rows preceding
NVIDIA Graphics / Intel Killer Network records.

Use the Windows CI release pipeline to validate these synthetic tests.
Physical Lenovo hardware, optional-module availability, live
`Find-LnvUpdate` output and actual applicability still require manual
Windows QA (see #14, #85 and #136). Do not count public catalog metadata
as an available per-device update or include it in `Update All`.
