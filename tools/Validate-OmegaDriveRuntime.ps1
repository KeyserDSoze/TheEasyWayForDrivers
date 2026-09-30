param(
    [switch]$Deep,
    [string]$JsonOutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$results = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [ValidateSet("Pass", "Warn", "Fail", "Info")]
        [string]$Status,
        [Parameter(Mandatory = $true)]
        [string]$Detail,
        [bool]$Critical = $false
    )

    $results.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Critical = $Critical
        Detail = $Detail
    })
}

function Test-FileSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Check -Name "$Name signature" -Status "Fail" -Critical $true -Detail "File missing: $Path"
        return
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $Path

    if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        $subject = if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { "unknown signer" }
        Add-Check -Name "$Name signature" -Status "Pass" -Detail "Valid Authenticode signature: $subject"
        return
    }

    if ($signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned) {
        Add-Check -Name "$Name signature" -Status "Warn" -Detail "Unsigned. Suitable for development testing, not recommended for public distribution."
        return
    }

    Add-Check -Name "$Name signature" -Status "Fail" -Critical $true -Detail "Signature status: $($signature.Status). $($signature.StatusMessage)"
}

if (-not $IsWindows) {
    Add-Check -Name "Operating system" -Status "Fail" -Critical $true -Detail "This validation script must run on Windows."
}
else {
    $osVersion = [Environment]::OSVersion.Version
    $isWindows11 = $osVersion.Build -ge 22000
    Add-Check -Name "Windows 11" -Status $(if ($isWindows11) { "Pass" } else { "Fail" }) -Critical $true -Detail "Detected Windows build $($osVersion.Build)."

    Add-Check -Name "64-bit operating system" -Status $(if ([Environment]::Is64BitOperatingSystem) { "Pass" } else { "Fail" }) -Critical $true -Detail "OmegaDrive release binaries currently target win-x64."

    $installRoot = Join-Path $env:ProgramFiles "TheEasyWayForDrivers"
    $servicePath = Join-Path $installRoot "Service\App1.Service.exe"
    $desktopPath = Join-Path $installRoot "Desktop\App2.Desktop.exe"
    $updaterPath = Join-Path $installRoot "Updater\OmegaDrive-Setup.exe"

    foreach ($file in @(
        @{ Name = "Service executable"; Path = $servicePath },
        @{ Name = "Desktop executable"; Path = $desktopPath },
        @{ Name = "Updater executable"; Path = $updaterPath }
    )) {
        if (Test-Path -LiteralPath $file.Path -PathType Leaf) {
            $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($file.Path)
            Add-Check -Name $file.Name -Status "Pass" -Critical $true -Detail "$($file.Path) · version $($info.FileVersion)"
        }
        else {
            Add-Check -Name $file.Name -Status "Fail" -Critical $true -Detail "Missing: $($file.Path)"
        }
    }

    try {
        $service = Get-Service -Name "TheEasyWayForDrivers.Service" -ErrorAction Stop
        Add-Check -Name "Windows Service" -Status $(if ($service.Status -eq "Running") { "Pass" } else { "Fail" }) -Critical $true -Detail "Status: $($service.Status)"
    }
    catch {
        Add-Check -Name "Windows Service" -Status "Fail" -Critical $true -Detail $_.Exception.Message
    }

    try {
        $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='TheEasyWayForDrivers.Service'" -ErrorAction Stop

        if ($null -eq $serviceInfo) {
            Add-Check -Name "Service configuration" -Status "Fail" -Critical $true -Detail "Service registration not found."
        }
        else {
            $expected = [IO.Path]::GetFullPath($servicePath)
            $configured = $serviceInfo.PathName.Trim('"')
            $pathMatches = $configured.IndexOf($expected, [StringComparison]::OrdinalIgnoreCase) -ge 0
            Add-Check -Name "Service configuration" -Status $(if ($pathMatches -and $serviceInfo.StartMode -eq "Auto") { "Pass" } else { "Fail" }) -Critical $true -Detail "StartMode=$($serviceInfo.StartMode); PathName=$($serviceInfo.PathName)"
        }
    }
    catch {
        Add-Check -Name "Service configuration" -Status "Fail" -Critical $true -Detail $_.Exception.Message
    }

    try {
        $device = Get-CimInstance Win32_PnPEntity -ErrorAction Stop | Select-Object -First 1
        Add-Check -Name "WMI Plug and Play inventory" -Status $(if ($null -ne $device) { "Pass" } else { "Fail" }) -Critical $true -Detail $(if ($null -ne $device) { "Win32_PnPEntity query succeeded." } else { "No Plug and Play device returned." })
    }
    catch {
        Add-Check -Name "WMI Plug and Play inventory" -Status "Fail" -Critical $true -Detail $_.Exception.Message
    }

    try {
        $session = New-Object -ComObject Microsoft.Update.Session
        Add-Check -Name "Windows Update Agent COM" -Status "Pass" -Critical $true -Detail "Microsoft.Update.Session created successfully."

        if ($Deep) {
            $searcher = $session.CreateUpdateSearcher()
            $searchResult = $searcher.Search("IsInstalled=0 and Type='Driver'")
            Add-Check -Name "Windows Update driver search" -Status "Pass" -Critical $true -Detail "Search completed. Driver updates returned: $($searchResult.Updates.Count)."
        }
        else {
            Add-Check -Name "Windows Update driver search" -Status "Info" -Detail "Skipped. Re-run with -Deep to execute a real driver search."
        }
    }
    catch {
        Add-Check -Name "Windows Update Agent COM" -Status "Fail" -Critical $true -Detail $_.Exception.Message
    }

    $settingsPath = Join-Path $env:LOCALAPPDATA "OmegaDrive\settings.json"

    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        try {
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -ErrorAction Stop
            Add-Check -Name "OmegaDrive settings" -Status "Pass" -Detail "Loaded $settingsPath; FirstRunCompleted=$($settings.FirstRunCompleted); CheckOnStartup=$($settings.CheckOnStartup)."
        }
        catch {
            Add-Check -Name "OmegaDrive settings" -Status "Warn" -Detail "Settings file exists but could not be parsed: $($_.Exception.Message)"
        }
    }
    else {
        Add-Check -Name "OmegaDrive settings" -Status "Warn" -Detail "No settings file yet. Complete the first-run experience before final validation."
    }

    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $startupValue = (Get-ItemProperty -Path $runKey -Name "TheEasyWayForDrivers" -ErrorAction SilentlyContinue)."TheEasyWayForDrivers"
    Add-Check -Name "Startup preference" -Status "Info" -Detail $(if ([string]::IsNullOrWhiteSpace($startupValue)) { "Startup with Windows is disabled for the current user." } else { "Startup with Windows is enabled: $startupValue" })

    Test-FileSignature -Name "Service" -Path $servicePath
    Test-FileSignature -Name "Desktop" -Path $desktopPath
    Test-FileSignature -Name "Updater" -Path $updaterPath

    $rollbackRoot = Join-Path $env:ProgramData "TheEasyWayForDrivers\Rollback"
    Add-Check -Name "Rollback snapshot" -Status "Info" -Detail $(if (Test-Path -LiteralPath $rollbackRoot -PathType Container) { "Rollback snapshot directory present: $rollbackRoot" } else { "No rollback snapshot yet. It is created when an installed version is updated." })
}

$results | Format-Table Status, Critical, Name, Detail -AutoSize | Out-Host

if (-not [string]::IsNullOrWhiteSpace($JsonOutputPath)) {
    $directory = Split-Path -Parent $JsonOutputPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [ordered]@{
        product = "OmegaDrive Driver Manager"
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        deep = [bool]$Deep
        checks = $results
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $JsonOutputPath -Encoding utf8NoBOM

    Write-Host "JSON report written to $JsonOutputPath"
}

$criticalFailures = @($results | Where-Object { $_.Critical -and $_.Status -eq "Fail" }).Count

if ($criticalFailures -gt 0) {
    Write-Error "$criticalFailures critical runtime validation check(s) failed."
    exit 1
}

Write-Host "OmegaDrive runtime validation completed without critical failures."
exit 0
