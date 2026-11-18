# OEM manual preview: bounded WUA/PnP evidence (#557)

## Bug

`VendorSourceDiscovery.FindWithCoverage` accepted arbitrary lazy/saved
PnP and Type=Driver WUA enumerables, feeding both to the deduplicator and
GUID/ID index without first applying the raw diagnostic budgets already
enforced by the eight-component QA matrix. Large or hostile snapshots
could waste resources and make an absent WUA match look like a measured
zero despite having processed an incomplete input.

## Fix

- PnP: materialize **at most 8193 raw rows** (the 8192+1 overflow
  sentinel). Apply the shared `ReferenceDriverCoverage` gate before
  dedup or WUA enumeration: 8192 raw, 2048 normalized instances, 262144
  PnP hardware/compatible IDs. Over-budget PnP yields **no trusted OEM
  preview**, no incomplete counts, and an anonymous not-verifiable note.
- WUA: inspect **at most 4001 raw rows** and check the same 2,000,000
  logical-ID ceiling (per-entry inspection cap included) before GUID
  merging and building the indexed matcher. If it exceeds budget,
  local PnP-only manual support handoffs remain visible, but the
  preview and each row explicitly state **WUA correlations not
  evaluable**. No zero match is represented as proven absence.
- `VendorManualHandoffSummary` propagates the new flag so the main
  results panel never says "0 with WUA candidate" when the WUA source
  comparison did not run.
- Normal verified scans retain the vendor-fair 100-row preview and
  exact-ID GUID correlation. No vendor title/URL invents an update.

## Validation boundaries

Synthetic Core xUnit verifies bounded infinite enumerable consumption,
raw/instance/PnP-ID quotas, WUA row and ID quotas, the partial summary,
valid exact-ID matches and empty verified inventories. Hardware/vendor
catalog QA (#91/#136) and Windows installed E2E (#85) remain open.
This is read-only; no OEM package download or installation.
