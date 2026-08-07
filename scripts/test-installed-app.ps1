[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$InstallerPath,
    [string]$InstallDirectory = (Join-Path $env:SystemDrive "codex-build\cas-installed-test"),
    [int]$CdpPort = 19224
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$safeRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemDrive "codex-build"))
$target = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $target.StartsWith($safeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallDirectory must remain under $safeRoot"
}
if (Test-Path -LiteralPath $target) {
    throw "InstallDirectory already exists: $target"
}

$testId = [guid]::NewGuid().ToString("N")
$userData = Join-Path $safeRoot "cas-e2e-userdata-$testId"
$backendData = Join-Path $safeRoot "cas-e2e-backend-$testId"
$codexHome = Join-Path $safeRoot "cas-e2e-codex-$testId\.codex"
New-Item -ItemType Directory -Path $userData, $backendData, $codexHome -Force | Out-Null
Set-Content -LiteralPath (Join-Path $codexHome "config.toml") -Value 'model = "test"' -Encoding UTF8
$sentinel = Join-Path $userData ("uninstall-preservation-$testId.txt")
Set-Content -LiteralPath $sentinel -Value "preserve" -Encoding ASCII
$previousEndpoint = $env:ELECTRON_CDP_URL
$previousE2E = $env:CODEX_SWITCHER_E2E
$previousE2EUserData = $env:CODEX_SWITCHER_E2E_USER_DATA
$previousBackendData = $env:CODEX_SWITCHER_DATA_DIR
$previousCodexHome = $env:CODEX_HOME
$backendVersion = $null

try {
    $install = Start-Process -FilePath $installer -ArgumentList @("/S", "/D=$target") -PassThru -Wait -WindowStyle Hidden
    if ($install.ExitCode -ne 0) { throw "Installer failed with exit code $($install.ExitCode)." }

    $application = Join-Path $target "Codex Account Switcher.exe"
    $backend = Join-Path $target "resources\backend\CodexAccountSwitcher.exe"
    $uninstaller = Join-Path $target "Uninstall Codex Account Switcher.exe"
    foreach ($required in @($application, $backend, $uninstaller)) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Installed file is missing: $required" }
    }
    $backendVersion = (Get-Item -LiteralPath $backend).VersionInfo.ProductVersion

    $env:CODEX_SWITCHER_E2E = "1"
    $env:CODEX_SWITCHER_E2E_USER_DATA = $userData
    $env:CODEX_SWITCHER_DATA_DIR = $backendData
    $env:CODEX_HOME = $codexHome
    $rootProcess = Start-Process -FilePath $application `
        -ArgumentList "--remote-debugging-port=$CdpPort", "--user-data-dir=$userData" `
        -Environment @{
            CODEX_SWITCHER_E2E = "1"
            CODEX_SWITCHER_E2E_USER_DATA = $userData
            CODEX_SWITCHER_DATA_DIR = $backendData
            CODEX_HOME = $codexHome
        } `
        -PassThru
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
        $rootState = Get-Process -Id $rootProcess.Id -ErrorAction SilentlyContinue
        Write-Warning ("Installed app readiness diagnostics: rootPid={0}; running={1}; exitCode={2}" -f `
            $rootProcess.Id, ($null -ne $rootState), $(if ($rootProcess.HasExited) { $rootProcess.ExitCode } else { "n/a" }))
        Get-CimInstance Win32_Process |
            Where-Object { $_.ExecutablePath -like "$target\*" } |
            Select-Object ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine |
            Format-List |
            Out-String |
            Write-Warning
        throw "Installed Electron app did not become ready."
    }

    $env:ELECTRON_CDP_URL = "http://127.0.0.1:$CdpPort"
    Push-Location (Join-Path $root "desktop")
    try {
        pnpm exec playwright test
        if ($LASTEXITCODE -ne 0) { throw "Packaged Electron E2E tests failed." }
    } finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath (Join-Path $backendData "switcher.db"))) {
        throw "Installed backend did not use the isolated test database."
    }

    Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -like "$target\*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 1

    $remove = Start-Process -FilePath $uninstaller -ArgumentList "/S" -PassThru -Wait -WindowStyle Hidden
    if ($remove.ExitCode -ne 0) { throw "Uninstaller failed with exit code $($remove.ExitCode)." }
    if (-not (Test-Path -LiteralPath $sentinel)) { throw "Uninstall removed Electron user data." }
    $deadline = (Get-Date).AddSeconds(30)
    do { Start-Sleep -Milliseconds 500 } until (-not (Test-Path -LiteralPath $target) -or (Get-Date) -ge $deadline)
    if (Test-Path -LiteralPath $target) { throw "Uninstaller did not remove the test install directory." }

    [pscustomobject]@{
        InstallerExitCode = $install.ExitCode
        BackendProductVersion = $backendVersion
        E2E = "passed"
        UserDataPreserved = $true
    } | ConvertTo-Json
} finally {
    if ($null -eq $previousEndpoint) { Remove-Item Env:ELECTRON_CDP_URL -ErrorAction SilentlyContinue } else { $env:ELECTRON_CDP_URL = $previousEndpoint }
    if ($null -eq $previousE2E) { Remove-Item Env:CODEX_SWITCHER_E2E -ErrorAction SilentlyContinue } else { $env:CODEX_SWITCHER_E2E = $previousE2E }
    if ($null -eq $previousE2EUserData) { Remove-Item Env:CODEX_SWITCHER_E2E_USER_DATA -ErrorAction SilentlyContinue } else { $env:CODEX_SWITCHER_E2E_USER_DATA = $previousE2EUserData }
    if ($null -eq $previousBackendData) { Remove-Item Env:CODEX_SWITCHER_DATA_DIR -ErrorAction SilentlyContinue } else { $env:CODEX_SWITCHER_DATA_DIR = $previousBackendData }
    if ($null -eq $previousCodexHome) { Remove-Item Env:CODEX_HOME -ErrorAction SilentlyContinue } else { $env:CODEX_HOME = $previousCodexHome }
    Get-CimInstance Win32_Process |
        Where-Object { $_.ExecutablePath -like "$target\*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $remainingUninstaller = Join-Path $target "Uninstall Codex Account Switcher.exe"
    if (Test-Path -LiteralPath $remainingUninstaller) {
        $cleanup = Start-Process -FilePath $remainingUninstaller -ArgumentList "/S" -PassThru -Wait -WindowStyle Hidden
        if ($cleanup.ExitCode -ne 0) { Write-Warning "Cleanup uninstaller returned $($cleanup.ExitCode)." }
    }
    foreach ($cleanupPath in @($userData, $backendData, (Split-Path -Parent $codexHome))) {
        $resolvedCleanup = [IO.Path]::GetFullPath($cleanupPath)
        if ($resolvedCleanup.StartsWith($safeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
