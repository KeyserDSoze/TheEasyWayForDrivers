Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$versionScript = Join-Path $PSScriptRoot 'Get-NextOmegaDriveVersion.ps1'

function Assert-NextVersion {
    param(
        [string[]]$Tags,
        [string]$Expected
    )

    $actual = & $versionScript -ReleaseTags $Tags
    if ($actual -ne $Expected) {
        throw "Release version error: expected $Expected but got $actual."
    }
}

Assert-NextVersion -Tags @() -Expected '0.0.1'
Assert-NextVersion -Tags @('v0.0.1') -Expected '0.0.2'
Assert-NextVersion -Tags @('v0.0.101') -Expected '0.0.102'
Assert-NextVersion -Tags @(
    'v0.0.99', 'v0.0.100', 'v0.0.101', 'v0.0.5') -Expected '0.0.102'
Assert-NextVersion -Tags @(
    'v0.1.3', 'v0.0.999', 'v0.1.2', 'v0.2.0') -Expected '0.2.1'
Assert-NextVersion -Tags @(
    'v0.0.42', 'v0.0.11', 'v0.0.7-beta', 'not-a-tag', 'v0.0.41') -Expected '0.0.43'
Assert-NextVersion -Tags @(
    'v0.0.100', 'v0.0.102', 'v0.0.101', 'v9999999999999.0.0',
    'v0.0.1-rc1') -Expected '0.0.103'

# Regression: gh release list --limit 100 does not include v0.0.1
# after 101+ releases. The old code falsely reset to v0.0.1 and
# tried to DELETE v0.0.2 before running into an existing v0.0.1.
$hundredReleases = @(2..101 | ForEach-Object { "v0.0.$_" })
if ($hundredReleases -contains 'v0.0.1') {
    throw 'Test construction error: v0.0.1 should be beyond the window.'
}
Assert-NextVersion -Tags $hundredReleases -Expected '0.0.102'

$failed = $false
try {
    & $versionScript -ReleaseTags @('garbage', 'v0.0.7-beta') | Out-Null
}
catch {
    $failed = $_.Exception.Message -match 'SemVer valido'
}
if (-not $failed) {
    throw 'Unknown version tags must fail closed, not overwrite release history.'
}

Write-Host 'OmegaDrive release SemVer regression suite passed.'
