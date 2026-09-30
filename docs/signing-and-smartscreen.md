# Authenticode and SmartScreen

## Release policy

OmegaDrive supports Authenticode signing through the GitHub repository secrets
`CODE_SIGNING_PFX_BASE64` and `CODE_SIGNING_PASSWORD`.

When those secrets are present, CI signs:

- `App1.Service.exe`;
- `App2.Desktop.exe`;
- `OmegaDrive-Setup.exe`.

CI then verifies those signatures with SignTool using the Authenticode
verification policy. A failed verification blocks the release.

The byte-identical legacy setup alias is created only after the canonical setup
has been signed, so it retains the same embedded signature.

## SmartScreen expectations

A valid Authenticode signature establishes publisher identity and lets
publisher reputation accumulate, but it does not guarantee that a brand-new
release will avoid a SmartScreen warning.

Unsigned releases have to build file reputation again for each changed binary.
Self-signed certificates are appropriate only for development or managed
enterprise trust, not public distribution.

For public distribution, use a certificate/service whose chain is trusted by
Windows and keep the signing identity consistent between releases.

## Current project state

Until signing secrets are configured, OmegaDrive releases are intentionally
marked as unsigned in the generated build manifest and GitHub Actions summary.

The repository currently does not contain a software license file. The setup UI
therefore reports the license as not yet defined instead of implying an
open-source license that has not been selected.
