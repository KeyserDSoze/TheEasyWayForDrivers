param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$Commit,
    [Parameter(Mandatory = $true)]
    [string]$ServicePath,
    [Parameter(Mandatory = $true)]
    [string]$DesktopPath,
    [Parameter(Mandatory = $true)]
    [string]$SetupPath,
    [Parameter(Mandatory = $true)]
    [string]$LegacySetupPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-ArtifactInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Role,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Artifact not found: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($item.FullName)
    $signature = Get-AuthenticodeSignature -LiteralPath $item.FullName

    [ordered]@{
        role = $Role
        name = $item.Name
        sizeBytes = $item.Length
        sha256 = $hash
        fileVersion = $versionInfo.FileVersion
        signatureStatus = $signature.Status.ToString()
        signerSubject = if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        timestampSubject = if ($null -ne $signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null }
    }
}

$artifacts = @(
    Get-ArtifactInfo -Role "service" -Path $ServicePath
    Get-ArtifactInfo -Role "desktop" -Path $DesktopPath
    Get-ArtifactInfo -Role "setup" -Path $SetupPath
    Get-ArtifactInfo -Role "legacy-setup-alias" -Path $LegacySetupPath
)

$manifest = [ordered]@{
    schemaVersion = 1
    product = "OmegaDrive Driver Manager"
    version = $Version
    commit = $Commit
    generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
    allSignaturesValid = @($artifacts | Where-Object { $_.signatureStatus -ne "Valid" }).Count -eq 0
    artifacts = $artifacts
}

$directory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Build manifest written to $OutputPath"
