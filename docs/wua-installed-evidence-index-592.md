# WUA-to-PnP installed evidence: indexed, validated associations (#592)

`VendorDriverEvidenceFinder.FindWithCoverage` already enforces the same raw
PnP and WUA budgets as the eight-category audit (#591), but the Home
installed-version view was still checking every device against every
confirmed WUA GUID.

`WuaInstalledEvidenceIndex` builds a case-insensitive, exact-ID lookup
once per scan. It indexes only IDs accepted by
`DriverUpdateMatcher.IsCorrelatableHardwareId`, deduplicated inside
each WUA GUID, with the same additional-ID cap as the authoritative
matcher. Each per-device lookup unions GUID indices across *both*
HardwareIds and CompatibleIds, then **rechecks** the shortlist with
`WuaDiagnosticAssociation.Matches`. NVIDIA/Intel/Killer names, PCI
vendor prefixes, weak model-only hints and metadata are never used
to manufacture a match.

An unusual snapshot that exceeds 250,000 indexed GUID-ID associations
falls back to the original exhaustive match. This trades execution
cost for bounded index memory without suppressing a real correlation.
The input row/ID limits and the Home's explicit
"non valutabile" states from #591 remain active.

These counts are diagnostic only. They do not prove a newer compatible
driver, alter QuickUpdatePolicy, change install eligibility, query
manufacturer catalogs or resolve physical Recoil 16 QA (#91/#111/#136).
