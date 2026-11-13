# Executes the EXACT embedded Desktop PowerShell queries on Windows
# with synthetic HP/Lenovo companion commands. No real OEM module, network,
# downloads, installers, WUA queries or registry changes are needed.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $hostExe)) {
    throw 'Windows PowerShell non disponibile per il gate OEM.'
}

function Read-EmbeddedQuery([string]$relative) {
    $path = Join-Path $root $relative
    $source = Get-Content -LiteralPath $path -Raw
    $m = [regex]::Match($source, 'private const string Query = """(?<query>[\s\S]*?)""";')
    if (-not $m.Success) { throw "Query PowerShell non trovata: $relative" }
    $script = $m.Groups['query'].Value
    if ($script -match 'Select-Object\s+-First\s+(80|120)\b') {
        throw "Il Desktop tronca il catalogo prima della selezione per categoria: $relative"
    }
    if ($script -notmatch 'Select-Object\s+-First\s+1001\b' -or
        $script -notmatch '\.Count\s+-gt\s+1000') {
        throw "Sentinella overflow mancante: $relative"
    }
    return $script
}

$lenovo = Read-EmbeddedQuery 'src/App2.Desktop/Services/LenovoApplicableUpdateReader.cs'
$lenovoCatalog = Read-EmbeddedQuery 'src/App2.Desktop/Services/LenovoCatalogReader.cs'
$hp = Read-EmbeddedQuery 'src/App2.Desktop/Services/HpSoftpaqCatalogReader.cs'

# The Core parser must NEVER accept a wider prefix class than the HP
# PowerShell reader actually requested. Keep their exact category sets
# in parity so new upstream HP categories require two explicit changes.
$parserSource = Get-Content -LiteralPath (
    Join-Path $root 'src/Shared.Core/Drivers/HpSoftpaqCatalogParser.cs') -Raw
$coreBlock = [regex]::Match($parserSource,
    'KnownDriverCategories\s*=\s*new\([^)]*\)\s*\{(?<rows>[\s\S]*?)\};')
if (-not $coreBlock.Success) {
    throw 'HP: allowlist delle categorie Core non trovata.'
}
$coreCategories = @(
    [regex]::Matches($coreBlock.Groups['rows'].Value, '"(?<name>[^"]+)"') |
        ForEach-Object { $_.Groups['name'].Value }
)
$hpCategories = @(
    [regex]::Matches($hp, "'(?<name>Driver(?: - [^']+)?)'") |
        ForEach-Object { $_.Groups['name'].Value } |
        Sort-Object -Unique
)
if ($coreCategories.Count -ne 9 -or $hpCategories.Count -ne 9 -or
    @($coreCategories | Sort-Object -Unique).Count -ne 9 -or
    (Compare-Object -ReferenceObject ($coreCategories | Sort-Object) `
        -DifferenceObject ($hpCategories | Sort-Object) -CaseSensitive)) {
    throw 'HP: categorie driver del reader e del parser fuori allineamento.'
}

# Runtime OEM applicability tools run independently of the metadata scripts
# above. Their redirected output must also be bounded BEFORE buffering.
# This gate is compiled into the existing Windows release/PR workflow.
foreach ($relative in @(
    'src/App2.Desktop/Services/HpImageAssistantReader.cs',
    'src/App2.Desktop/Services/DellApplicableUpdateReader.cs'
)) {
    $source = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
    if ($source -match 'ReadToEndAsync') {
        throw "OEM output senza lettura bounded: $relative"
    }
    $bounded = [regex]::Matches($source,
        'OemCompanionOutputReader\.ReadBoundedAsync\(')
    if ($bounded.Count -ne 2) {
        throw "OEM stdout/stderr devono usare la lettura bounded: $relative"
    }
    foreach ($stream in @('StandardOutput', 'StandardError')) {
        $pattern = 'process\.' + $stream + ',\s*32\s*\*\s*1024'
        if ($source -notmatch $pattern) {
            throw "Limite 32 KiB mancante per $stream in $relative"
        }
    }
    if ($source -notmatch 'Task\.WhenAll\(') {
        throw "OEM non drena i pipe in parallelo: $relative"
    }
}

$moduleRoot = Join-Path ([IO.Path]::GetTempPath()) ("OmegaDrive-Companion-Test-" + [Guid]::NewGuid().ToString('N'))
$originalModules = $env:PSModulePath
$originalCount = $env:OMEGA_COMPANION_TEST_ROWS
try {
    $lenovoModule = Join-Path $moduleRoot 'Lenovo.Client.Update'
    New-Item -ItemType Directory -Path $lenovoModule -Force | Out-Null
    @'
function Get-LnvUpdate {
    [CmdletBinding()]
    param()
    $count = [int]$env:OMEGA_COMPANION_TEST_ROWS
    for ($i = 0; $i -lt $count; $i++) {
        [pscustomobject]@{
            PackageID = "audio$i"; Title = "Driver Audio $i"
            Type = 2; Category = 'Audio'
            IsApplicable = $true; IsInstalled = $false
            Version = '1.0.0.0'; ReleaseDate = '2026-11-09'
            Severity = 'Recommended'
        }
    }
    if ($count -lt 1000) {
        [pscustomobject]@{
            PackageID = 'late-nvidia'; Title = 'NVIDIA Graphics'
            Type = 2; Category = 'Graphics'
            IsApplicable = $true; IsInstalled = $false
            Version = '2.0.0.0'; ReleaseDate = '2026-11-09'
            Severity = 'Recommended'
        }
        [pscustomobject]@{
            PackageID = 'late-killer'; Title = 'Killer Network'
            Type = 2; Category = 'Network'
            IsApplicable = $true; IsInstalled = $false
            Version = '2.0.0.0'; ReleaseDate = '2026-11-09'
            Severity = 'Critical'
        }
    }
}
Export-ModuleMember -Function Get-LnvUpdate
'@ | Set-Content -LiteralPath (Join-Path $lenovoModule 'Lenovo.Client.Update.psm1') -Encoding UTF8

    # Lenovo.Client.Scripting is a distinct, public MachineType catalog
    # from Lenovo.Client.Update's current-PC applicability Get-LnvUpdate.
    $catalogModule = Join-Path $moduleRoot 'Lenovo.Client.Scripting'
    New-Item -ItemType Directory -Path $catalogModule -Force | Out-Null
    @'
function Find-LnvUpdate {
    [CmdletBinding()]
    param([string]$MachineType, [string]$WindowsVersion,
          [string]$PackageType, [switch]$ListAll)
    if ($MachineType -ne '20YZ' -or $WindowsVersion -ne '11' -or
        $PackageType -ne '2' -or -not $ListAll) {
        throw 'Lenovo metadata query parameters changed.'
    }
    $count = [int]$env:OMEGA_COMPANION_TEST_ROWS
    for ($i = 0; $i -lt $count; $i++) {
        [pscustomobject]@{
            ID = "audio$i"; Name = "Audio catalog $i"
            Category = 'Audio'; PackageType = 2
            Version = '1.0'; ReleaseDate = '2026-11-09'
        }
    }
    if ($count -lt 1000) {
        [pscustomobject]@{
            ID = 'late-nvidia'; Name = 'NVIDIA Graphics'
            Category = 'Graphics'; PackageType = 2
            Version = '2.0'; ReleaseDate = '2026-11-09'
        }
        [pscustomobject]@{
            ID = 'late-killer'; Name = 'Killer Network'
            Category = 'Network'; PackageType = 2
            Version = '2.0'; ReleaseDate = '2026-11-09'
        }
    }
}
Export-ModuleMember -Function Find-LnvUpdate
'@ | Set-Content -LiteralPath (Join-Path $catalogModule 'Lenovo.Client.Scripting.psm1') -Encoding UTF8
    $env:OMEGADRIVE_LENOVO_MACHINE_TYPE = '20YZ'

    $hpMock = @'
function Get-HPSoftpaqList {
    [CmdletBinding()]
    param([string[]]$Category, [switch]$Quiet)
    $count = [int]$env:OMEGA_COMPANION_TEST_ROWS
    for ($i = 0; $i -lt $count; $i++) {
        [pscustomobject]@{
            Id = "sp$i"; Name = "Audio driver $i"
            Version = '1.0'; ReleaseType = 'Recommended'
            ReleaseDate = '2026-11-09'; Category = 'Driver - Audio'
        }
    }
    if ($count -lt 1000) {
        [pscustomobject]@{
            Id = 'late-nvidia'; Name = 'NVIDIA GPU'
            Version = '2.0'; ReleaseType = 'Recommended'
            ReleaseDate = '2026-11-09'; Category = 'Driver - Graphics'
        }
        [pscustomobject]@{
            Id = 'late-killer'; Name = 'Killer Network'
            Version = '2.0'; ReleaseType = 'Recommended'
            ReleaseDate = '2026-11-09'; Category = 'Driver - Network'
        }
    }
}
'@

    $env:PSModulePath = $moduleRoot + [IO.Path]::PathSeparator + $originalModules

    function Invoke-FakeQuery([string]$query, [string]$prelude) {
        $code = $prelude + [Environment]::NewLine + $query
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
        $output = @(& $hostExe -NoProfile -NonInteractive -EncodedCommand $encoded 2>&1)
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Text = $output -join [Environment]::NewLine
        }
    }

    $env:OMEGA_COMPANION_TEST_ROWS = '130'
    foreach ($sample in @(
        @{ Name='Lenovo'; Script=$lenovo; Prelude=''; Key='PackageID' },
        @{ Name='Lenovo catalog'; Script=$lenovoCatalog; Prelude=''; Key='ID' },
        @{ Name='HP'; Script=$hp; Prelude=$hpMock; Key='Id' }
    )) {
        $check = Invoke-FakeQuery $sample.Script $sample.Prelude
        if ($check.ExitCode -ne 0) {
            throw "$($sample.Name): script fallito nel test sintetico: $($check.Text)"
        }
        $rows = @($check.Text | ConvertFrom-Json)
        if ($rows.Count -ne 132) {
            throw "$($sample.Name): attese 132 righe, ottenute $($rows.Count)"
        }
        $keys = @($rows | ForEach-Object { $_.($sample.Key) })
        if ('late-nvidia' -notin $keys -or 'late-killer' -notin $keys) {
            throw "$($sample.Name): driver Graphics/Network nascosti dopo l'ottantesima/centoventesima riga."
        }
    }

    $env:OMEGA_COMPANION_TEST_ROWS = '1001'
    foreach ($sample in @(
        @{ Name='Lenovo'; Script=$lenovo; Prelude='' },
        @{ Name='Lenovo catalog'; Script=$lenovoCatalog; Prelude='' },
        @{ Name='HP'; Script=$hp; Prelude=$hpMock }
    )) {
        $check = Invoke-FakeQuery $sample.Script $sample.Prelude
        if ($check.ExitCode -eq 0) {
            throw "$($sample.Name): il catalogo >1000 righe è stato falsamente dichiarato completo."
        }
        if ($check.Text -notmatch 'Troppi driver') {
            throw "$($sample.Name): errore overflow non riconoscibile."
        }
    }

    # The last expected negative process ends with exit code 1. Reset it
    # after asserting failure, or the GitHub pwsh action would fail anyway.
    $global:LASTEXITCODE = 0
    Write-Host 'Gate OEM: HP, Lenovo applicable e Lenovo catalog mantengono categorie tardive; overflow fail-closed.'
}
finally {
    $env:PSModulePath = $originalModules
    $env:OMEGA_COMPANION_TEST_ROWS = $originalCount
    if (Test-Path -LiteralPath $moduleRoot) {
        Remove-Item -LiteralPath $moduleRoot -Recurse -Force
    }
}
