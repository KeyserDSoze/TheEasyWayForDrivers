# #554 — WUA source error list uses verified COM row provenance

The main Home warning lists unavailable driver sources via
`WuaSourceFailurePolicy.UnavailableSources`. Before #554 it accepted a
syntactically valid `Completed` pass solely by its status, even when its
`Candidates` raw COM count was lower than the number of WUA GUID rows
attributed to the same source in a saved/legacy IPC snapshot.

Other diagnostic surfaces and the confirmed-driver merge already used
`IsVerifiablePassForReport`, which also validates source-local raw
counts. The Home unavailable-source list now uses that same gate, so
a contradictory Completed source is **listed as unverified**, without
displaying its private `Detail` or source name from untrusted input.

Tests verify both 0 COM rows with one reported GUID, 1 with two,
healthy independent WUA sources remaining available, valid Microsoft
Update NotRegistered never labeled a failure, and unknown source
names being replaced with a compiled-in neutral label.

This changes only read-only diagnostic presentation. It does not
change WUA search, installer selection, network/policy settings or
the eight-candidate hardware benchmark. Real offline/WSUS/managed
Windows QA remains under #92 and complete E2E under #85.
