[CmdletBinding()]
param(
    [AllowEmptyCollection()]
    [string[]]$ReleaseTags = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The GitHub release list is a bounded RECENT window. It may no longer
# contain v0.0.1 after more than 100 releases: never treat that as a
# brand-new repository. Numeric max handles mixed ordering and v0.0.99
# versus v0.0.100 correctly.
if ($ReleaseTags.Count -eq 0) {
    Write-Output '0.0.1'
    return
}

$latest = $null
foreach ($tag in $ReleaseTags) {
    if ($tag -notmatch '^v([0-9]+)\.([0-9]+)\.([0-9]+)$') {
        # Ignore drafts, prerelease tags or releases of a different
        # product. A repository with no valid stable tags fails closed.
        continue
    }

    try {
        $parsed = [version]$tag.Substring(1)
    }
    catch {
        continue
    }

    if ($null -eq $latest -or $parsed -gt $latest) {
        $latest = $parsed
    }
}

if ($null -eq $latest) {
    throw 'Nessun tag OmegaDrive SemVer valido: versione non verificabile.'
}

if ($latest.Build -eq [int]::MaxValue) {
    throw 'Versione OmegaDrive oltre il limite numerico.'
}

$next = '{0}.{1}.{2}' -f $latest.Major, $latest.Minor, ($latest.Build + 1)
Write-Output $next
