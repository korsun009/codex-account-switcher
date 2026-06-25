param(
    [string]$TaskName = 'CodexStartRemoteApi',
    [string]$AppExePath,
    [string]$ApiPrefix = 'http://127.0.0.1:8765/',
    [string]$CodexHome,
    [string]$AllowedRemoteAddress,
    [int]$DelaySeconds = 5
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

if ([string]::IsNullOrWhiteSpace($AppExePath)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $AppExePath = Join-Path $repoRoot 'codex-account-switcher\bin\Release\net8.0-windows\win-x64\publish\CodexAccountSwitcher.exe'
}

if ([string]::IsNullOrWhiteSpace($CodexHome)) {
    $CodexHome = Join-Path $env:USERPROFILE '.codex'
}

if (-not (Test-Path -LiteralPath $AppExePath)) {
    throw "CodexAccountSwitcher executable was not found: $AppExePath"
}

$apiToken = [Environment]::GetEnvironmentVariable('CODEX_REMOTE_API_TOKEN', 'User')
if ([string]::IsNullOrWhiteSpace($apiToken)) {
    throw 'Set CODEX_REMOTE_API_TOKEN in the current Windows user environment before installing the task.'
}

[Environment]::SetEnvironmentVariable('CODEX_REMOTE_API_URL', $ApiPrefix, 'User')
[Environment]::SetEnvironmentVariable('CODEX_REMOTE_CODEX_HOME', $CodexHome, 'User')
[Environment]::SetEnvironmentVariable('CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS', $AllowedRemoteAddress, 'User')

$launcherDir = Join-Path $env:APPDATA 'CodexAccountSwitcher'
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
$launcherPath = Join-Path $launcherDir 'run-remote-api.ps1'

@"
`$ErrorActionPreference = 'Stop'
`$env:CODEX_REMOTE_API_TOKEN = [Environment]::GetEnvironmentVariable('CODEX_REMOTE_API_TOKEN', 'User')
`$env:CODEX_REMOTE_API_URL = [Environment]::GetEnvironmentVariable('CODEX_REMOTE_API_URL', 'User')
`$env:CODEX_REMOTE_CODEX_HOME = [Environment]::GetEnvironmentVariable('CODEX_REMOTE_CODEX_HOME', 'User')
`$env:CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS = [Environment]::GetEnvironmentVariable('CODEX_REMOTE_ALLOWED_REMOTE_ADDRESS', 'User')
& '$($AppExePath.Replace("'", "''"))' --remote-api
"@ | Set-Content -LiteralPath $launcherPath -Encoding UTF8

$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$launcherPath`"" `
    -WorkingDirectory (Split-Path -Parent $AppExePath)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
if ($DelaySeconds -gt 0) {
    $trigger.Delay = "PT${DelaySeconds}S"
}

$principalConfig = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 0)

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principalConfig `
    -Settings $settings `
    -Force | Out-Null

if (-not [string]::IsNullOrWhiteSpace($AllowedRemoteAddress)) {
    $apiUri = [Uri]$ApiPrefix
    $ruleName = 'Codex Remote API from home gateway'
    $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if ($existingRule) {
        Remove-NetFirewallRule -DisplayName $ruleName
    }

    New-NetFirewallRule `
        -DisplayName $ruleName `
        -Direction Inbound `
        -Action Allow `
        -Protocol TCP `
        -LocalPort $apiUri.Port `
        -RemoteAddress $AllowedRemoteAddress `
        -Profile Any | Out-Null
}

Get-ScheduledTask -TaskName $TaskName |
    Select-Object TaskName, State, @{ Name = 'RunLevel'; Expression = { $_.Principal.RunLevel } }, @{ Name = 'UserId'; Expression = { $_.Principal.UserId } }, @{ Name = 'LogonType'; Expression = { $_.Principal.LogonType } } |
    ConvertTo-Json -Compress
