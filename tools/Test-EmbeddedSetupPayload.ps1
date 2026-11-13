param(
    [Parameter(Mandatory = $true)]
    [string]$SetupPath,
    [Parameter(Mandatory = $true)]
    [string]$PayloadZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Embedded {
    param([bool]$Condition, [string]$Reason)
    if (-not $Condition) {
        throw "OmegaDrive embedded payload integrity failed: $Reason"
    }
}

Assert-Embedded (Test-Path -LiteralPath $SetupPath -PathType Leaf) "setup missing"
Assert-Embedded (Test-Path -LiteralPath $PayloadZip -PathType Leaf) "ZIP missing"
$setup = Get-Item -LiteralPath $SetupPath
$archive = Get-Item -LiteralPath $PayloadZip

# The runtime installer accepts at most 512 MiB ZIP. Limit the setup
# scan as well: malicious external inputs must not exhaust CI memory.
$maxZip = 512L * 1024 * 1024
$maxSetup = 1536L * 1024 * 1024
Assert-Embedded ($archive.Length -ge 22 -and $archive.Length -le $maxZip) "ZIP size invalid"
Assert-Embedded ($setup.Length -ge $archive.Length -and
    $setup.Length -le $maxSetup) "setup size invalid"

# .NET PublishSingleFile embeds the managed assembly and its embedded
# resources as unchanged bytes by default (compression disabled in the
# project). Compare the COMPLETE ZIP bytes, not just PK magic, CRC,
# basename or file size. This never loads or executes the elevated EXE.
# Future compressed single-file bundles need an equivalent bundle reader.
if (-not ("OmegaDrive.Release.EmbeddedZipByteScanner" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;

namespace OmegaDrive.Release
{
    public static class EmbeddedZipByteScanner
    {
        public static int CountExactPayloadOccurrences(string setup, string zip)
        {
            byte[] payload = File.ReadAllBytes(zip);
            if (payload.Length < 22 ||
                payload[0] != 0x50 || payload[1] != 0x4b ||
                payload[2] != 0x03 || payload[3] != 0x04)
                return 0;

            byte[] host = File.ReadAllBytes(setup);
            if (host.Length < payload.Length)
                return 0;

            int matches = 0;
            int offset = 0;
            while (offset <= host.Length - payload.Length)
            {
                int index = host.AsSpan(offset).IndexOf(payload.AsSpan());
                if (index < 0)
                    break;
                if (++matches > 1)
                    return matches;
                offset += index + 1;
            }
            return matches;
        }
    }
}
'@ -ErrorAction Stop
}

$count = [OmegaDrive.Release.EmbeddedZipByteScanner]::CountExactPayloadOccurrences(
    $setup.FullName, $archive.FullName)
Assert-Embedded ($count -eq 1) "ZIP bytes missing or duplicated inside setup"
Write-Host "OmegaDrive setup embedded payload integrity OK (one exact ZIP copy)."
