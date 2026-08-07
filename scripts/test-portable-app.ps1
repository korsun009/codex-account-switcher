#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StageRoot,
    [ValidateSet("x64", "ia32")] [string]$Architecture = "x64",
    [int]$CdpPort = 19500
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$release = Join-Path (Resolve-Path -LiteralPath $StageRoot).Path "desktop\release"
$unpackedName = if ($Architecture -eq "x64") { "win-unpacked" } else { "win-ia32-unpacked" }
$unpacked = Join-Path $release $unpackedName
$application = Join-Path $unpacked "Codex Account Switcher.exe"
if (-not (Test-Path -LiteralPath $application)) {
    throw "Portable application is missing: $application"
}

$safeRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemDrive "codex-build"))
$testId = [guid]::NewGuid().ToString("N")
$userData = Join-Path $safeRoot "cas-portable-user-$Architecture-$testId"
$backendData = Join-Path $safeRoot "cas-portable-data-$Architecture-$testId"
$codexRoot = Join-Path $safeRoot "cas-portable-codex-$Architecture-$testId"
$codexHome = Join-Path $codexRoot ".codex"
$previousEndpoint = $env:ELECTRON_CDP_URL
$previousExpected = $env:CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION

New-Item -ItemType Directory -Path $userData, $backendData, $codexHome -Force | Out-Null
Set-Content -LiteralPath (Join-Path $codexHome "config.toml") -Value 'model = "test"' -Encoding UTF8

try {
    Start-Process -FilePath $application `
        -ArgumentList "--remote-debugging-port=$CdpPort", "--user-data-dir=$userData" `
        -Environment @{
            CODEX_SWITCHER_E2E = "1"
            CODEX_SWITCHER_E2E_USER_DATA = $userData
            CODEX_SWITCHER_DATA_DIR = $backendData
            CODEX_HOME = $codexHome
        } | Out-Null

    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$CdpPort/json/list" -TimeoutSec 2
            $ready = @($targets | Where-Object { $_ -and $_.type -eq "page" }).Count -gt 0
        } catch {
            $ready = $false
        }
    } until ($ready -or (Get-Date) -ge $deadline)
    if (-not $ready) {
        throw "Portable $Architecture application did not expose its E2E renderer."
    }

    $env:ELECTRON_CDP_URL = "http://127.0.0.1:$CdpPort"
    Remove-Item Env:CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION -ErrorAction SilentlyContinue
    Push-Location (Join-Path $root "desktop")
    try {
        pnpm exec playwright test --grep-invert "checks and downloads a newer package"
        if ($LASTEXITCODE -ne 0) {
            throw "Portable $Architecture E2E tests failed."
        }
    } finally {
        Pop-Location
    }

    $database = Join-Path $backendData "switcher.db"
    if (-not (Test-Path -LiteralPath $database)) {
        throw "Portable $Architecture backend did not create its isolated database."
    }

    [pscustomobject]@{
        Architecture = $Architecture
        E2E = "passed"
        BackendDatabase = "created"
        Executable = $application
    } | ConvertTo-Json
} finally {
    Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -like "$unpacked\*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $env:ELECTRON_CDP_URL = $previousEndpoint
    $env:CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION = $previousExpected
    foreach ($path in @($userData, $backendData, $codexRoot)) {
        $resolved = [IO.Path]::GetFullPath($path)
        if ($resolved.StartsWith($safeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
