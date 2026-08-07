[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PreviousNsisInstaller,
    [Parameter(Mandatory)] [string]$CurrentNsisInstaller,
    [string]$InstallDirectory = (Join-Path $env:SystemDrive "codex-build\cas-upgrade-test")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

foreach ($path in @($PreviousNsisInstaller, $CurrentNsisInstaller)) {
    if ([IO.Path]::GetExtension($path) -ne ".exe") {
        throw "This harness accepts NSIS EXE installers only. Test the legacy v1 MSI in an isolated VM."
    }
}
$safeRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemDrive "codex-build"))
$target = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $target.StartsWith($safeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "InstallDirectory must remain under $safeRoot"
}
if (Test-Path -LiteralPath $target) { throw "InstallDirectory already exists: $target" }

$userData = Join-Path $env:APPDATA "codex-account-switcher-desktop"
New-Item -ItemType Directory -Path $userData -Force | Out-Null
$sentinel = Join-Path $userData ("upgrade-preservation-" + [guid]::NewGuid().ToString("N") + ".txt")
Set-Content -LiteralPath $sentinel -Value "preserve" -Encoding ASCII

try {
    foreach ($installer in @($PreviousNsisInstaller, $CurrentNsisInstaller)) {
        $resolved = (Resolve-Path -LiteralPath $installer).Path
        $process = Start-Process -FilePath $resolved -ArgumentList @("/S", "/D=$target") -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "Installer failed with exit code $($process.ExitCode): $resolved" }
        if (-not (Test-Path -LiteralPath $sentinel)) { throw "Upgrade removed Electron user data." }
    }

    $backend = Join-Path $target "resources\backend\CodexAccountSwitcher.exe"
    if (-not (Test-Path -LiteralPath $backend)) { throw "Upgraded backend is missing." }
    [pscustomobject]@{
        Upgrade = "passed"
        BackendProductVersion = (Get-Item -LiteralPath $backend).VersionInfo.ProductVersion
        UserDataPreserved = $true
    } | ConvertTo-Json
} finally {
    $uninstaller = Join-Path $target "Uninstall Codex Account Switcher.exe"
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList "/S" -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { Write-Warning "Test uninstaller returned $($process.ExitCode)." }
    }
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}
