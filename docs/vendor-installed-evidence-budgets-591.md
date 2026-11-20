# Bounded installed-driver evidence on Home (#591)

The NVIDIA/Intel/Killer installed-version panel was the last read-only
diagnostic display still consuming the entire supplied PnP and WUA
enumerables. Other OmegaDrive reports already cap the raw Windows
inventory at 8,192 rows, 2,048 distinct instances, 262,144 PnP IDs,
and confirmed WUA at 4,000 rows / 2,000,000 inspected IDs.

`VendorDriverEvidenceFinder.FindWithCoverage` now bounds the input
at the same limits **before** PnP identity consensus and WUA GUID merge.
Its returned `VendorDriverEvidencePreview` distinguishes
`InventoryComplete` from `CorrelationsComplete`.

* Over-budget PnP inventory: no rows are classified; Home clearly says
  the inventory cannot be evaluated, never "no supported devices".
* Over-budget WUA input: locally installed versions can still be
  inspected, but there are **no** WUA correlations or measured zeros;
  every row and the Home heading say they are not evaluable.
* Completed, within-budget sources preserve precise PnP matching,
  vendor-fair ordering and the existing privacy redaction. Historical,
  cached, partial and OEM metadata remain non-installable.

This does not prove new OEM driver availability or resolve #91/#111/#136,
the real-Windows E2E test #85 or unsigned installer #283.
