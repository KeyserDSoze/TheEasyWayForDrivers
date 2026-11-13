$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$verifier = Join-Path $PSScriptRoot "Test-EmbeddedSetupPayload.ps1"
$root = Join-Path ([IO.Path]::GetTempPath()) (
    "OmegaDrive-EmbeddedZipGate-" + [Guid]::NewGuid().ToString("N"))
$setup = Join-Path $root "OmegaDrive-Setup.exe"
$zipPath = Join-Path $root "payload.zip"

function Assert-Rejected {
    param([string]$Scenario)
    $rejected = $false
    try {
        & $verifier -SetupPath $setup -PayloadZip $zipPath | Out-Null
    }
    catch {
        $rejected = $_.Exception.Message -like (
            "OmegaDrive embedded payload integrity failed:*")
    }
    if (-not $rejected) {
        throw "Embedded ZIP gate regression: '$Scenario' was not rejected."
    }
}

function Write-HostFixture {
    param([byte[]]$EmbeddedZip, [int]$Copies = 1)
    $bytes = [IO.MemoryStream]::new()
    try {
        $prefix = [Text.Encoding]::ASCII.GetBytes("MZ fake host app bundle")
        $suffix = [Text.Encoding]::ASCII.GetBytes("end of host binary")
        $bytes.Write($prefix, 0, $prefix.Length)
        for ($i = 0; $i -lt $Copies; $i++) {
            $bytes.Write($EmbeddedZip, 0, $EmbeddedZip.Length)
        }
        $bytes.Write($suffix, 0, $suffix.Length)
        [IO.File]::WriteAllBytes($setup, $bytes.ToArray())
    }
    finally {
        $bytes.Dispose()
    }
}

try {
    New-Item -Path $root -ItemType Directory -Force | Out-Null

    $zip = [IO.Compression.ZipFile]::Open($zipPath,
        [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @(
            "Service/App1.Service.exe", "Desktop/App2.Desktop.exe")) {
            $entry = $zip.CreateEntry($name)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write("binary fixture - no executable is run") }
            finally { $writer.Dispose() }
        }
    }
    finally {
        $zip.Dispose()
    }
    [byte[]]$payload = [IO.File]::ReadAllBytes($zipPath)
    if ($payload.Length -lt 22) { throw "ZIP fixture too short." }

    Write-HostFixture -EmbeddedZip $payload
    & $verifier -SetupPath $setup -PayloadZip $zipPath | Out-Null

    Write-HostFixture -EmbeddedZip $payload -Copies 0
    Assert-Rejected "no embedded ZIP"

    Write-HostFixture -EmbeddedZip $payload -Copies 2
    Assert-Rejected "two embedded ZIP copies"

    $truncated = [byte[]]::new([int]($payload.Length - 1))
    [Array]::Copy($payload, $truncated, $truncated.Length)
    Write-HostFixture -EmbeddedZip $truncated
    Assert-Rejected "truncated embedded ZIP"

    Write-HostFixture -EmbeddedZip $payload
    $mutated = [byte[]]$payload.Clone()
    $mutated[24] = $mutated[24] -bxor 1
    [IO.File]::WriteAllBytes($zipPath, $mutated)
    Assert-Rejected "same-size ZIP with different bytes"
    [IO.File]::WriteAllBytes($zipPath, $payload)

    # Non-ZIP bytes cannot be accepted merely because they are present.
    [IO.File]::WriteAllBytes($zipPath,
        [Text.Encoding]::ASCII.GetBytes("not even a ZIP archive"))
    Assert-Rejected "non-ZIP file"
    [IO.File]::WriteAllBytes($zipPath, $payload)

    & $verifier -SetupPath $setup -PayloadZip $zipPath | Out-Null
    Write-Host "Embedded setup payload gate regression cases passed."
}
finally {
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force
    }
}
