# WUA cancellation takes precedence over recoverable pass errors (#589)

The `DriverSearchPassAudit.Run` boundary classifies each independent
read-only Windows Update pass as Completed, Partial, PolicyRestricted,
NetworkError or Failed. If the user explicitly cancels while the COM
provider reports an I/O error, policy/network HRESULT or a partial result,
the prior error-catching branches would still create a pass-failure status.

The boundary now checks the caller's cancellation token before invoking the
failure observer and again after it returns. A requested cancellation
propagates as OperationCanceledException, without incorrectly classifying
the pass as an unavailable driver source. A cancellation requested inside
the failure observer is also propagated; the observer cannot produce a
subsequent status. Critical OutOfMemoryException remains fail-fast even when
cancellation is set. Ordinary independent errors with no cancellation are
still classified as non-verified, never as zero available updates.

This does not query new OEM catalogs, download, stage or install drivers.
The Windows 11 offline/WSUS/policy hardware gate (#92/#85) remains open.
