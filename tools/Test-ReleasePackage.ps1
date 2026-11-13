param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactsDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommit
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Release {
    param([bool]$Condition, [string]$Reason)
    if (-not $Condition) {
        throw "OmegaDrive release integrity failed: $Reason"
    }
}

function File-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$root = [IO.Path]::GetFullPath($ArtifactsDirectory)
$manifestPath = Join-Path $root "OmegaDrive-build-manifest.json"
Assert-Release (Test-Path -LiteralPath $manifestPath -PathType Leaf) "manifest missing"
$manifest = Get-Content -LiteralPath $manifestPath -Raw |
    ConvertFrom-Json -ErrorAction Stop

Assert-Release ($manifest.schemaVersion -eq 1) "unexpected manifest schema"
Assert-Release ($manifest.product -ceq "OmegaDrive Driver Manager") "wrong product"
Assert-Release ($manifest.version -ceq $ExpectedVersion) "version differs from workflow"
Assert-Release ($ExpectedCommit -match '^[0-9a-fA-F]{40}$') "invalid workflow commit"
Assert-Release ($manifest.commit -ieq $ExpectedCommit) "commit differs from workflow"
Assert-Release ($manifest.allSignaturesValid -is [bool]) "signature summary is not a boolean"

$relativeByRole = @{
    "service" = "payload/Service/App1.Service.exe"
    "desktop" = "payload/Desktop/App2.Desktop.exe"
    "setup" = "OmegaDrive-Setup.exe"
    "legacy-setup-alias" = "TheEasyWayForDrivers-Setup.exe"
}
$roles = @("service", "desktop", "setup", "legacy-setup-alias")
$rows = @($manifest.artifacts)
Assert-Release ($rows.Count -eq $roles.Count) "missing or extra manifest roles"
$hashes = @{}
$signatureStatuses = @()

foreach ($role in $roles) {
    $matching = @($rows | Where-Object { $_.role -ceq $role })
    Assert-Release ($matching.Count -eq 1) "missing or duplicated role $role"
    $row = $matching[0]
    $filePath = Join-Path $root $relativeByRole[$role]
    Assert-Release (Test-Path -LiteralPath $filePath -PathType Leaf) "missing $role file"
    $file = Get-Item -LiteralPath $filePath
    Assert-Release ($file.Length -gt 0) "empty $role file"
    Assert-Release ($row.name -ceq $file.Name) "wrong $role filename"
    Assert-Release ([long]$row.sizeBytes -eq $file.Length) "wrong $role size"

    $digest = File-Sha256 $filePath
    Assert-Release ($row.sha256 -ceq $digest) "wrong $role SHA-256"
    $hashes[$role] = $digest

    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
        $file.FullName).FileVersion
    Assert-Release ([string]$row.fileVersion -ceq [string]$actualVersion) "wrong $role file version"

    # The manifest describes the actual binaries, not an assumed signing
    # state. Unsigned builds remain allowed; false 'Valid' claims do not.
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    $signatureStatuses += $signature.Status.ToString()
    Assert-Release ($row.signatureStatus -ceq $signature.Status.ToString()) "wrong $role signature status"
    $signer = if ($null -ne $signature.SignerCertificate) {
        $signature.SignerCertificate.Subject
    } else { $null }
    $timestamp = if ($null -ne $signature.TimeStamperCertificate) {
        $signature.TimeStamperCertificate.Subject
    } else { $null }
    Assert-Release ([string]$row.signerSubject -ceq [string]$signer) "wrong $role signer"
    Assert-Release ([string]$row.timestampSubject -ceq [string]$timestamp) "wrong $role timestamp"
}

$allSigned = @($signatureStatuses | Where-Object { $_ -ne "Valid" }).Count -eq 0
Assert-Release ($manifest.allSignaturesValid -eq $allSigned) "signature summary disagrees with files"
Assert-Release ($hashes["setup"] -ceq $hashes["legacy-setup-alias"]) "legacy setup differs from canonical setup"

foreach ($role in @("setup", "legacy-setup-alias")) {
    $name = Split-Path -Leaf $relativeByRole[$role]
    $sidecar = Join-Path $root ($name + ".sha256")
    Assert-Release (Test-Path -LiteralPath $sidecar -PathType Leaf) "missing $role SHA-256 sidecar"
    $line = (Get-Content -LiteralPath $sidecar -Raw).TrimEnd([char[]]@([char]13, [char]10))
    $expectedLine = $hashes[$role] + "  " + $name
    Assert-Release ($line -ceq $expectedLine) "wrong $role SHA-256 sidecar"
}

# The setup is published with an embedded copy of this payload ZIP.
# Before embedding, check EVERY payload file and every ZIP file entry,
# not just the two executable names recorded in the JSON manifest.
$payloadRoot = Join-Path $root "payload"
$zipPath = Join-Path $root "payload.zip"
Assert-Release (Test-Path -LiteralPath $payloadRoot -PathType Container) "payload folder missing"
Assert-Release (Test-Path -LiteralPath $zipPath -PathType Leaf) "payload ZIP missing"

$expectedFiles = [Collections.Generic.Dictionary[string,object]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($file in @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File)) {
    $name = [IO.Path]::GetRelativePath($payloadRoot, $file.FullName).Replace('\', '/')
    Assert-Release ($name -match '^(Service|Desktop)/') "payload contains an unexpected root"
    Assert-Release (-not $expectedFiles.ContainsKey($name)) "duplicate payload filename"
    $expectedFiles.Add($name, [pscustomobject]@{
        Length = $file.Length
        Hash = (File-Sha256 $file.FullName)
    })
}
Assert-Release ($expectedFiles.Count -ge 2) "payload has no files"
Assert-Release ($expectedFiles.ContainsKey("Service/App1.Service.exe")) "service missing from payload"
Assert-Release ($expectedFiles.ContainsKey("Desktop/App2.Desktop.exe")) "desktop missing from payload"

$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $seen = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if ($name.EndsWith('/')) {
            # Directory entries have no data; reject traversal and roots.
            Assert-Release (
                $name -match '^(Service|Desktop)/' -and
                -not ($name.Split('/') -contains '..')
            ) "unexpected ZIP directory"
            continue
        }
        Assert-Release ($expectedFiles.ContainsKey($name)) "unexpected ZIP payload file"
        Assert-Release ($seen.Add($name)) "duplicate ZIP payload file"
        $expected = $expectedFiles[$name]
        Assert-Release ($entry.Length -eq $expected.Length) "ZIP payload size mismatch"
        $stream = $entry.Open()
        try {
            $actualHash = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
        }
        Assert-Release ($actualHash -ceq $expected.Hash) "ZIP payload SHA-256 mismatch"
    }
    Assert-Release ($seen.Count -eq $expectedFiles.Count) "ZIP is missing payload files"
}
finally {
    $zip.Dispose()
}

Write-Host "OmegaDrive release integrity OK: manifest, signatures, setup aliases, sidecars and $($expectedFiles.Count) ZIP payload files."
