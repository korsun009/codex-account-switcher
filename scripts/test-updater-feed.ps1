#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$StageRoot,
    [string]$SimulatedVersion = "2.0.1",
    [int]$FeedPort = 19400,
    [int]$CdpPort = 19401
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$desktop = Join-Path $root "desktop"
$stage = (Resolve-Path -LiteralPath $StageRoot).Path
$release = Join-Path $stage "desktop\release"
$application = Join-Path $release "win-unpacked\Codex Account Switcher.exe"
$metadata = Join-Path $release "latest.yml"
$installer = Get-Item -LiteralPath (Join-Path $release "Codex-Account-Switcher-2.0.0-Setup.exe") -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $application) -or -not (Test-Path -LiteralPath $metadata) -or $null -eq $installer) {
    throw "Packaged application, latest.yml, or installer is missing under $release"
}

$safeRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemDrive "codex-build"))
$testId = [guid]::NewGuid().ToString("N")
$feed = Join-Path $safeRoot "cas-update-feed-$testId"
$userData = Join-Path $safeRoot "cas-update-user-$testId"
$backendData = Join-Path $safeRoot "cas-update-data-$testId"
$codexHome = Join-Path $safeRoot "cas-update-codex-$testId\.codex"
$serverOut = Join-Path $safeRoot "cas-update-server-$testId.out.log"
$serverErr = Join-Path $safeRoot "cas-update-server-$testId.err.log"
$server = $null

New-Item -ItemType Directory -Path $feed, $userData, $backendData, $codexHome -Force | Out-Null
Set-Content -LiteralPath (Join-Path $codexHome "config.toml") -Value 'model = "test"' -Encoding UTF8
Get-ChildItem -LiteralPath $release -File |
    Where-Object { $_.Name -like "Codex-Account-Switcher-*-Setup.exe" -or $_.Name -like "Codex-Account-Switcher-*-Setup.exe.blockmap" } |
    Copy-Item -Destination $feed -Force
$feedMetadata = (Get-Content -Raw -LiteralPath $metadata) -replace '(?m)^version:\s*.+$', "version: $SimulatedVersion"
Set-Content -LiteralPath (Join-Path $feed "latest.yml") -Value $feedMetadata -Encoding UTF8

$appUpdate = Join-Path $release "win-unpacked\resources\app-update.yml"
if (Test-Path -LiteralPath $appUpdate) {
    $isolatedUpdateConfig = (Get-Content -Raw -LiteralPath $appUpdate) -replace '(?m)^updaterCacheDirName:\s*.+$', "updaterCacheDirName: codex-account-switcher-e2e-$testId"
    Set-Content -LiteralPath $appUpdate -Value $isolatedUpdateConfig -Encoding UTF8
}

$previousCdp = $env:ELECTRON_CDP_URL
$previousExpected = $env:CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION
try {
    $node = (Get-Command node -ErrorAction Stop).Source
    $serverScript = Join-Path $desktop "tests\e2e\update-feed-server.mjs"
    $server = Start-Process -FilePath $node `
        -ArgumentList @("`"$serverScript`"", "`"$feed`"", "$FeedPort") `
        -RedirectStandardOutput $serverOut `
        -RedirectStandardError $serverErr `
        -WindowStyle Hidden `
        -PassThru

    $feedUrl = "http://127.0.0.1:$FeedPort/"
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        try {
            $probe = Invoke-WebRequest -Uri "${feedUrl}latest.yml" -TimeoutSec 2
            $feedReady = $probe.StatusCode -eq 200
        } catch {
            $feedReady = $false
        }
    } until ($feedReady -or $server.HasExited -or (Get-Date) -ge $deadline)
    if (-not $feedReady) {
        $errorText = if (Test-Path -LiteralPath $serverErr) { Get-Content -Raw -LiteralPath $serverErr } else { "" }
        throw "Update feed did not become ready. $errorText"
    }

    Start-Process -FilePath $application `
        -ArgumentList "--remote-debugging-port=$CdpPort", "--user-data-dir=$userData" `
        -Environment @{
            CODEX_SWITCHER_E2E = "1"
            CODEX_SWITCHER_E2E_USER_DATA = $userData
            CODEX_SWITCHER_E2E_UPDATE_URL = $feedUrl
            CODEX_SWITCHER_DATA_DIR = $backendData
            CODEX_HOME = $codexHome
        } | Out-Null

    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        try {
            $targets = Invoke-RestMethod -Uri "http://127.0.0.1:$CdpPort/json/list" -TimeoutSec 2
            $appReady = @($targets | Where-Object type -eq "page").Count -gt 0
        } catch {
            $appReady = $false
        }
    } until ($appReady -or (Get-Date) -ge $deadline)
    if (-not $appReady) {
        throw "Packaged application did not expose its E2E renderer."
    }

    $env:ELECTRON_CDP_URL = "http://127.0.0.1:$CdpPort"
    $env:CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION = $SimulatedVersion
    Push-Location $desktop
    try {
        pnpm exec playwright test -g "checks and downloads a newer package"
        if ($LASTEXITCODE -ne 0) {
            throw "Updater E2E test failed."
        }
    } finally {
        Pop-Location
    }

    [pscustomobject]@{
        Result = "passed"
        CurrentVersion = "2.0.0"
        AvailableVersion = $SimulatedVersion
        Feed = $feedUrl
        DownloadVerifiedBySha512 = $true
        InstallerBytes = $installer.Length
    } | ConvertTo-Json
} finally {
    Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -like "$release\win-unpacked\*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($null -ne $server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    }
    $env:ELECTRON_CDP_URL = $previousCdp
    $env:CODEX_SWITCHER_E2E_UPDATE_EXPECTED_VERSION = $previousExpected
    foreach ($path in @($feed, $userData, $backendData, (Split-Path -Parent $codexHome))) {
        $resolved = [IO.Path]::GetFullPath($path)
        if ($resolved.StartsWith($safeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -LiteralPath $serverOut, $serverErr -Force -ErrorAction SilentlyContinue
}
