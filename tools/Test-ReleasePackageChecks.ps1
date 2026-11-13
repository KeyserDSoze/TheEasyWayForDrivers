$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$verifier = Join-Path $PSScriptRoot "Test-ReleasePackage.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) (
    "OmegaDrive-ReleaseIntegrity-" + [Guid]::NewGuid().ToString("N"))
$version = "9.9.9"
$commit = "a" * 40

function Assert-Rejected {
    param([string]$Scenario)
    $rejected = $false
    try {
        & $verifier -ArtifactsDirectory $root -ExpectedVersion $version -ExpectedCommit $commit | Out-Null
    }
    catch {
        $rejected = $_.Exception.Message -like "OmegaDrive release integrity failed:*"
    }
    if (-not $rejected) {
        throw "Release gate regression: '$Scenario' was not rejected."
    }
}

try {
    $payload = Join-Path $root "payload"
    $service = Join-Path $payload "Service/App1.Service.exe"
    $desktop = Join-Path $payload "Desktop/App2.Desktop.exe"
    $setup = Join-Path $root "OmegaDrive-Setup.exe"
    $legacy = Join-Path $root "TheEasyWayForDrivers-Setup.exe"
    $zipPath = Join-Path $root "payload.zip"
    $manifestPath = Join-Path $root "OmegaDrive-build-manifest.json"

    foreach ($dir in @($root, (Split-Path $service),
            (Split-Path $desktop))) {
        New-Item -Path $dir -ItemType Directory -Force | Out-Null
    }
    # Tiny synthetic EXE fixtures: no binary is executed or signed.
    [IO.File]::WriteAllBytes($service,
        [Text.Encoding]::UTF8.GetBytes("fixture-service-1"))
    [IO.File]::WriteAllBytes($desktop,
        [Text.Encoding]::UTF8.GetBytes("fixture-desktop-1"))
    [IO.File]::WriteAllBytes($setup,
        [Text.Encoding]::UTF8.GetBytes("fixture-setup-1"))
    Copy-Item -LiteralPath $setup -Destination $legacy
    Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $zipPath

    $parts = @(
        @{ Role = "service"; Path = $service },
        @{ Role = "desktop"; Path = $desktop },
        @{ Role = "setup"; Path = $setup },
        @{ Role = "legacy-setup-alias"; Path = $legacy }
    )
    $rows = @(
        foreach ($part in $parts) {
            $file = Get-Item -LiteralPath $part.Path
            $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
            [ordered]@{
                role = $part.Role
                name = $file.Name
                sizeBytes = $file.Length
                sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo(
                    $file.FullName).FileVersion
                signatureStatus = $signature.Status.ToString()
                signerSubject = if ($signature.SignerCertificate) {
                    $signature.SignerCertificate.Subject
                } else { $null }
                timestampSubject = if ($signature.TimeStamperCertificate) {
                    $signature.TimeStamperCertificate.Subject
                } else { $null }
            }
        }
    )
    $manifest = [ordered]@{
        schemaVersion = 1
        product = "OmegaDrive Driver Manager"
        version = $version
        commit = $commit
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        allSignaturesValid = @($rows | Where-Object {
            $_.signatureStatus -ne "Valid"
        }).Count -eq 0
        artifacts = $rows
    }

    function Save-Manifest {
        $manifest | ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    }
    Save-Manifest
    $digest = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach ($name in @("OmegaDrive-Setup.exe",
            "TheEasyWayForDrivers-Setup.exe")) {
        ($digest + "  " + $name) |
            Set-Content -LiteralPath (Join-Path $root ($name + ".sha256")) -Encoding ascii
    }

    # A complete, internally consistent package must pass.
    & $verifier -ArtifactsDirectory $root -ExpectedVersion $version -ExpectedCommit $commit | Out-Null

    [IO.File]::WriteAllBytes($legacy,
        [Text.Encoding]::UTF8.GetBytes("fixture-tampered-legacy"))
    Assert-Rejected "legacy setup differs"
    Copy-Item -LiteralPath $setup -Destination $legacy -Force

    $sidecar = Join-Path $root "OmegaDrive-Setup.exe.sha256"
    Set-Content -LiteralPath $sidecar -Encoding ascii -Value (
        ("0" * 64) + "  OmegaDrive-Setup.exe")
    Assert-Rejected "invalid SHA-256 sidecar"
    ($digest + "  OmegaDrive-Setup.exe") |
        Set-Content -LiteralPath $sidecar -Encoding ascii

    $manifest["commit"] = "b" * 40
    Save-Manifest
    Assert-Rejected "stale manifest commit"
    $manifest["commit"] = $commit
    Save-Manifest

    # The workflow's supplied version is an independent trust anchor.
    $version = "9.9.8"
    Assert-Rejected "wrong workflow version"
    $version = "9.9.9"

    $manifest["artifacts"][3]["role"] = "setup"
    Save-Manifest
    Assert-Rejected "duplicate manifest roles"
    $manifest["artifacts"][3]["role"] = "legacy-setup-alias"
    Save-Manifest

    # An archive with unexpected data must fail even when all manifest
    # hashes/sidecars are individually correct.
    $originalZip = [IO.File]::ReadAllBytes($zipPath)
    $zip = [IO.Compression.ZipFile]::Open($zipPath,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $extra = $zip.CreateEntry("Service/rogue.exe")
        $writer = [IO.StreamWriter]::new($extra.Open())
        try { $writer.Write("rogue payload") }
        finally { $writer.Dispose() }
    }
    finally { $zip.Dispose() }
    Assert-Rejected "extra ZIP payload file"
    [IO.File]::WriteAllBytes($zipPath, $originalZip)

    # Same filename and byte count, different contents: a name/size-only
    # ZIP gate would incorrectly accept this package.
    $zip = [IO.Compression.ZipFile]::Open($zipPath,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.GetEntry("Desktop/App2.Desktop.exe")
        if ($null -eq $entry) { throw "Fixture ZIP missing desktop entry." }
        $entry.Delete()
        $replacement = $zip.CreateEntry("Desktop/App2.Desktop.exe")
        $writer = [IO.StreamWriter]::new($replacement.Open())
        try { $writer.Write("fixture-desktop-X") }
        finally { $writer.Dispose() }
    }
    finally { $zip.Dispose() }
    Assert-Rejected "ZIP payload content differs but size matches"
    [IO.File]::WriteAllBytes($zipPath, $originalZip)

    $zip = [IO.Compression.ZipFile]::Open($zipPath,
        [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.GetEntry("Service/App1.Service.exe")
        if ($null -eq $entry) { throw "Fixture ZIP missing service entry." }
        $entry.Delete()
    }
    finally { $zip.Dispose() }
    Assert-Rejected "ZIP missing service executable"

    Write-Host "Release package integrity regression tests passed."
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
