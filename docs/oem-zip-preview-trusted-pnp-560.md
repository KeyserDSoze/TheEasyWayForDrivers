# ZIP OEM INF preview: trusted PnP identity consensus (#560)

## Bug
The static ZIP inspection dialog displayed every raw WMI/SignedDriver
row as an independent exact INF hardware match. Duplicated Windows
DeviceIds with contradictory primary hardware models (even under the
same vendor), different SUBSYS values, or missing instance IDs could
create false INF matches despite the main WUA audit and the full local
OEM comparison explicitly rejecting the same evidence.

## Fix
- Retain the raw 2048-row snapshot boundary, add the shared 262144-ID
  diagnostic budget **before** allocating per-device ID sets.
- Resolve the snapshot with `PnpDistinctTrustedDevices.Create` prior to
  all INF comparisons: ASCII-padded/case aliases share one logical
  instance; contradictions in primary VEN/DEV, SUBSYS or vendor fail
  closed. No PnP tokens are merged across independent rows.
- Report `DevicesExamined` (raw), `TrustedDevices` (distinct accepted)
  and `ExcludedUnverifiedPnpIdentities` (discarded distinct identities
  plus missing-instance raw rows). The Desktop inspection result gives
  an anonymous non-conclusive warning if exclusions occurred.
- Both the 500-ID metadata preview and full targeted INF match mode
  apply this final per-device PnP trust boundary, without claiming
  CAT signature, SetupAPI rank or installability.

## Tests and limits
Synthetic ZIP tests cover NVIDIA/Realtek contradictory same-instance
identities, two NVIDIA models, disagreeing SUBSYS, same-device padded
duplicates, mixed valid Killer device, blank DeviceId, malformed
multi-model row, >262144 IDs, infinitely iterable raw PnP inputs and
targeted matches after the first 500 preview IDs.

No OEM package is executed or installed. Real hardware testing with
PCSpecialist Recoil 16 and its actual order driver archive remains in
#124/#91/#136; Windows GUI installed E2E in #85. All matching remains
read-only potential evidence.
