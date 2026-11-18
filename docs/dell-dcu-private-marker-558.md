# Dell Command Update: component IDs and private metadata (#558)

## Issue
A Dell DCU XML scan evaluates the current machine. The parser previously
accepted a valid-looking ASCII Component ID containing a local `SERIAL_`,
`ORDER_`, `CUSTOMER_` or PnP `VEN_`/`DEV_` marker. Because this ID
is displayed in `VendorSourceCheck.Evidence`, a saved/legacy DCU
response could leak a device/order identifier even without a path.
Title, Version and Severity did not redact many such markers, and
an invisible Unicode format character could split a literal marker.

## Change
- Reject the **entire** Dell component identity if it contains any
  common private or PnP instance marker; never repair/truncate into
  an existing valid ID. Public ASCII component tokens and GUIDs remain.
- For all displayed fields, inspect the *full* original string before
  the 160-character display limit. Strip Unicode control/format
  characters only from the privacy inspection copy, so values like
  `OR<U+200B>DER_...` are treated as private. Continue to preserve
  readable international titles without private information.
- Apply identical checks when a caller supplies `Package` objects
  directly via `ToVendorChecks`, not just parsed XML.
- Existing duplicate-ID preference for a later public title is
  maintained. Type=BIOS/Firmware and unknown formats remain excluded,
  and Dell report content never becomes a WUA `Update All` candidate.

## Verification and remaining QA
Synthetic xUnit cases cover sensitive IDs, metadata beyond 160
characters, Unicode-invisible private markers, direct package calls,
safe GUID/ASCII IDs, multilingual titles and duplicate handling.

The validation neither queries live Dell DCU nor attests applicability
or package signatures. Real Dell Command Update 5.x scans remain
unverified (#104), and end-to-end installed Windows QA remains #85.
