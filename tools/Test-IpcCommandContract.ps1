# Static release gate for the Desktop -> Windows Service IPC command contract.
# This catches missing switch handlers; it does NOT replace real Windows IPC tests.
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$clientPath = Join-Path $root "src/App2.Desktop/Services/DriverServiceClient.cs"
$servicePath = Join-Path $root "src/App1.Service/Ipc/IpcCommandDispatcher.cs"

$clientSource = Get-Content -LiteralPath $clientPath -Raw
$serviceSource = Get-Content -LiteralPath $servicePath -Raw

# Commands declared with a literal in new IpcRequest("command", ...)
$clientCommands = @(
    [regex]::Matches($clientSource, '\bnew\s+IpcRequest\s*\(\s*"(?<command>[^"]+)"') |
        ForEach-Object { $_.Groups["command"].Value }
)

# Commands routed by the service dispatcher (request.Command).
$serviceCommands = @(
    [regex]::Matches($serviceSource, '(?m)^\s*"(?<command>[^"]+)"\s*=>') |
        ForEach-Object { $_.Groups["command"].Value }
)

# Explicit inventory: adding/removing IPC surface requires reviewed contract changes.
$expectedCommands = @(
    "scan",
    "search-updates",
    "search-report",
    "install-updates",
    "oem-providers",
    "check-app-update",
    "diagnostics"
)

if ($clientCommands.Count -eq 0 -or $serviceCommands.Count -eq 0) {
    throw "IPC contract could not be parsed. Update this guard when changing the command representation."
}

$clientDuplicates = @($clientCommands | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
$serviceDuplicates = @($serviceCommands | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)

$missingHandlers = @($clientCommands | Where-Object { $_ -notin $serviceCommands })
$unusedHandlers = @($serviceCommands | Where-Object { $_ -notin $clientCommands })
$missingRequired = @($expectedCommands | Where-Object { $_ -notin $clientCommands -or $_ -notin $serviceCommands })
$allCommands = @($clientCommands) + @($serviceCommands)
$unreviewedCommands = @(
    $allCommands |
        Where-Object { $_ -notin $expectedCommands } |
        Sort-Object -Unique
)

$problems = @()
if ($clientDuplicates.Count -gt 0) { $problems += "Duplicate client commands: $($clientDuplicates -join ', ')" }
if ($serviceDuplicates.Count -gt 0) { $problems += "Duplicate service handlers: $($serviceDuplicates -join ', ')" }
if ($missingHandlers.Count -gt 0) { $problems += "Missing service handlers: $($missingHandlers -join ', ')" }
if ($unusedHandlers.Count -gt 0) { $problems += "Unhandled service-only commands: $($unusedHandlers -join ', ')" }
if ($missingRequired.Count -gt 0) { $problems += "Required commands missing: $($missingRequired -join ', ')" }
if ($unreviewedCommands.Count -gt 0) { $problems += "New commands require inventory review: $($unreviewedCommands -join ', ')" }

if ($problems.Count -gt 0) {
    throw ("IPC command contract failed:`n - " + ($problems -join "`n - "))
}

Write-Host "IPC command contract OK: $($expectedCommands -join ', ')"

# #550: privileged service IPC errors must never reflect provider exception
# text, arbitrary command names or Windows logon/executable paths. The
# structural gate verifies the named-pipe SERVER uses the tested policy.
$pipeServer = Get-Content -LiteralPath (
    Join-Path $root 'src/App1.Service/Ipc/NamedPipeServer.cs') -Raw
$disclosure = Get-Content -LiteralPath (
    Join-Path $root 'src/App1.Service/Ipc/IpcTransportDisclosurePolicy.cs') -Raw
if ($pipeServer.Contains('IpcMessage.Error(exception.Message)') -or
    $pipeServer.Contains('GetClientIdentity(') -or
    $pipeServer.Contains('clientProcess.ExecutablePath') -or
    $pipeServer.Contains('logger.LogError(exception,') -or
    $pipeServer.Contains('logger.LogWarning(exception,') -or
    -not $pipeServer.Contains('IpcTransportDisclosurePolicy.ToClientError(exception)') -or
    -not $pipeServer.Contains('IpcTransportDisclosurePolicy.SafeCommandLabel(') -or
    -not $pipeServer.Contains('catch (JsonException)') -or
    -not $pipeServer.Contains('IpcTransportDisclosurePolicy.InvalidRequest') -or
    -not $disclosure.Contains('return error is ArgumentException or JsonException') -or
    -not $disclosure.Contains('KnownCommands.Contains(command)')) {
    throw 'IPC transport must use fixed non-identifying error frames and allowlisted log labels.'
}
