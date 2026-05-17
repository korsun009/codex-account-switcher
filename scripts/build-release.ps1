param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "v1.0.1"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $root "release\$Version"
$assets = Join-Path $releaseRoot "assets"
$source = Join-Path $releaseRoot "source"

New-Item -ItemType Directory -Force -Path $assets, $source | Out-Null

dotnet test (Join-Path $root "codex-account-switcher\CodexAccountSwitcher.sln")
dotnet publish (Join-Path $root "codex-account-switcher\codex-account-switcher.csproj") -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true
dotnet publish (Join-Path $root "codex-account-switcher\codex-account-switcher.csproj") -c Release -r win-x86 -p:PublishSingleFile=true -p:SelfContained=true
dotnet build (Join-Path $root "codex-account-switcher\Installer\CodexAccountSwitcher.Installer.wixproj") -c Release
dotnet build (Join-Path $root "codex-account-switcher\InstallerX86\CodexAccountSwitcher.InstallerX86.wixproj") -c Release

Copy-Item (Join-Path $root "codex-account-switcher\Installer\bin\Release\CodexAccountSwitcherSetup.msi") (Join-Path $assets "CodexAccountSwitcherSetup-win-x64.msi") -Force
Copy-Item (Join-Path $root "codex-account-switcher\InstallerX86\bin\Release\CodexAccountSwitcherSetup-x86.msi") (Join-Path $assets "CodexAccountSwitcherSetup-win-x86.msi") -Force
Compress-Archive -Path (Join-Path $root "codex-account-switcher\bin\Release\net8.0-windows\win-x64\publish\CodexAccountSwitcher.exe") -DestinationPath (Join-Path $assets "CodexAccountSwitcher-portable-win-x64.zip") -Force
Compress-Archive -Path (Join-Path $root "codex-account-switcher\bin\Release\net8.0-windows\win-x86\publish\CodexAccountSwitcher.exe") -DestinationPath (Join-Path $assets "CodexAccountSwitcher-portable-win-x86.zip") -Force

$temp = Join-Path $env:TEMP ("codex-account-switcher-source-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
$exclude = "\\(bin|obj|\.vs|release|chat-backup|_account_profiles|_account_switcher_backups)($|\\)"
Get-ChildItem -Path $root -Recurse -Force | Where-Object {
    -not $_.PSIsContainer -and $_.FullName -notmatch $exclude -and $_.Name -notmatch "\.db$|\.db-shm$|\.db-wal$|\.log$|auth\.json$"
} | ForEach-Object {
    $relative = $_.FullName.Substring($root.Length).TrimStart("\")
    $target = Join-Path $temp $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
}
Compress-Archive -Path (Join-Path $temp "*") -DestinationPath (Join-Path $source "CodexAccountSwitcher-source-$Version.zip") -Force
Remove-Item -LiteralPath $temp -Recurse -Force

Get-ChildItem -File -Path $assets, $source | ForEach-Object {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
    "$($hash.Hash)  $($_.Name)"
} | Set-Content -Path (Join-Path $releaseRoot "SHA256SUMS.txt") -Encoding ASCII

Copy-Item (Join-Path $root "release\v1.0.1\RELEASE_NOTES.md") (Join-Path $releaseRoot "RELEASE_NOTES.md") -Force -ErrorAction SilentlyContinue
Write-Host "Release package created at $releaseRoot"
