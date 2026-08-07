[CmdletBinding()]
param(
    [string]$StageRoot = (Join-Path $env:SystemDrive ("codex-build\stage-" + (Get-Date -Format "yyyyMMdd-HHmmss"))),
    [switch]$SkipTests,
    [switch]$AllowUnsigned
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ($StageRoot -match "[^\x00-\x7F]") {
    throw "StageRoot must contain ASCII characters only: $StageRoot"
}
if (Test-Path -LiteralPath $StageRoot) {
    if (@(Get-ChildItem -LiteralPath $StageRoot -Force).Count -gt 0) {
        throw "StageRoot must be empty or absent: $StageRoot"
    }
} else {
    New-Item -ItemType Directory -Path $StageRoot -Force | Out-Null
}

function Copy-SourceTree {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string[]]$ExcludedDirectoryNames
    )

    Get-ChildItem -LiteralPath $Source -Recurse -File -Force | ForEach-Object {
        $sourcePrefix = $Source.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $relative = $_.FullName.Substring($sourcePrefix.Length)
        $segments = $relative -split "[\\/]"
        if (@($segments | Where-Object { $_ -in $ExcludedDirectoryNames }).Count -gt 0) {
            return
        }
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string]$FailureMessage,
        [Parameter(Mandatory)] [scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

$backendSource = Join-Path $root "codex-account-switcher"
$desktopSource = Join-Path $root "desktop"
$backendStage = Join-Path $StageRoot "codex-account-switcher"
$desktopStage = Join-Path $StageRoot "desktop"

Copy-SourceTree -Source $backendSource -Destination $backendStage -ExcludedDirectoryNames @("bin", "obj", ".vs")
Copy-SourceTree -Source $desktopSource -Destination $desktopStage -ExcludedDirectoryNames @("node_modules", "out", "release", "test-results", "playwright-report")

if (-not $SkipTests) {
    Invoke-Checked -FailureMessage "Backend tests failed." -Command {
        dotnet test (Join-Path $backendStage "CodexAccountSwitcher.Tests\CodexAccountSwitcher.Tests.csproj") --configuration Release --verbosity minimal
    }
}

$backendTargets = [ordered]@{
    x64  = "win-x64"
    ia32 = "win-x86"
}
foreach ($entry in $backendTargets.GetEnumerator()) {
    $backendArtifact = Join-Path $StageRoot ("artifacts\backend\" + $entry.Key)
    Invoke-Checked -FailureMessage "Backend publish failed for $($entry.Value)." -Command {
        dotnet publish (Join-Path $backendStage "codex-account-switcher.csproj") `
            --configuration Release `
            --runtime $entry.Value `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            --output $backendArtifact `
            --verbosity minimal
    }
}

Push-Location $desktopStage
try {
    Invoke-Checked -FailureMessage "pnpm install failed." -Command { pnpm install --frozen-lockfile }
    if (-not $SkipTests) {
        Invoke-Checked -FailureMessage "TypeScript validation failed." -Command { pnpm typecheck }
        Invoke-Checked -FailureMessage "Electron tests failed." -Command { pnpm test }
    }
    Invoke-Checked -FailureMessage "Electron packaging failed." -Command { pnpm package:win }
} finally {
    Pop-Location
}

$stageRelease = Join-Path $desktopStage "release"
$requiredPackages = @(
    "Codex-Account-Switcher-2.0.0-Setup.exe",
    "Codex-Account-Switcher-2.0.0-x64-Setup.exe",
    "Codex-Account-Switcher-2.0.0-ia32-Setup.exe",
    "Codex-Account-Switcher-2.0.0-x64-Setup.msi",
    "Codex-Account-Switcher-2.0.0-ia32-Setup.msi"
)
foreach ($name in $requiredPackages) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageRelease $name))) {
        throw "Required Windows package was not produced: $name"
    }
}

$unpackedByArchitecture = [ordered]@{
    x64  = Join-Path $stageRelease "win-unpacked"
    ia32 = Join-Path $stageRelease "win-ia32-unpacked"
}
foreach ($entry in $unpackedByArchitecture.GetEnumerator()) {
    $executable = Join-Path $entry.Value "Codex Account Switcher.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Packaged application executable was not produced for $($entry.Key)."
    }
    $zipPath = Join-Path $stageRelease "Codex-Account-Switcher-2.0.0-$($entry.Key)-Portable.zip"
    Compress-Archive -Path (Join-Path $entry.Value "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force
}

$updateMetadata = Join-Path $stageRelease "latest.yml"
if (-not (Test-Path -LiteralPath $updateMetadata)) {
    throw "Updater metadata latest.yml was not produced."
}
$requiredBlockMaps = @(
    "Codex-Account-Switcher-2.0.0-Setup.exe.blockmap",
    "Codex-Account-Switcher-2.0.0-x64-Setup.exe.blockmap",
    "Codex-Account-Switcher-2.0.0-ia32-Setup.exe.blockmap"
)
foreach ($name in $requiredBlockMaps) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageRelease $name))) {
        throw "Required updater blockmap was not produced: $name"
    }
}

$signedFiles = @(
    $requiredPackages | ForEach-Object { Join-Path $stageRelease $_ }
) + @(
    $unpackedByArchitecture.Values | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -Filter "*.exe" -File -Recurse | Select-Object -ExpandProperty FullName
    }
)
$signatureResults = @($signedFiles | ForEach-Object {
    $result = Get-AuthenticodeSignature -LiteralPath $_
    $releasePrefix = $stageRelease.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    [pscustomobject]@{
        File = $_.Substring($releasePrefix.Length)
        Status = [string]$result.Status
    }
})
$invalidSignatures = @($signatureResults | Where-Object Status -ne "Valid")
if ($invalidSignatures.Count -gt 0 -and -not $AllowUnsigned) {
    $summary = ($invalidSignatures | ForEach-Object { "$($_.File): $($_.Status)" }) -join "; "
    throw "Production release requires valid Authenticode signatures: $summary"
}

$output = Join-Path $desktopSource "release"
New-Item -ItemType Directory -Path $output -Force | Out-Null
$knownReleasePatterns = @(
    "Codex-Account-Switcher-*-Setup.exe",
    "Codex-Account-Switcher-*-Setup.exe.blockmap",
    "Codex-Account-Switcher-*-Setup.msi",
    "Codex-Account-Switcher-*-Portable.zip",
    "latest.yml",
    "SHA256SUMS.txt"
)
foreach ($pattern in $knownReleasePatterns) {
    Get-ChildItem -LiteralPath $output -Filter $pattern -File -ErrorAction SilentlyContinue | Remove-Item -Force
}

$releaseAssetNames = @($requiredPackages) + @($requiredBlockMaps) + @(
    "Codex-Account-Switcher-2.0.0-x64-Portable.zip",
    "Codex-Account-Switcher-2.0.0-ia32-Portable.zip",
    "latest.yml"
)
$publishedAssets = @()
foreach ($name in $releaseAssetNames) {
    $destination = Join-Path $output $name
    Copy-Item -LiteralPath (Join-Path $stageRelease $name) -Destination $destination -Force
    $publishedAssets += $destination
}

$checksumPath = Join-Path $output "SHA256SUMS.txt"
$checksumLines = $publishedAssets | Sort-Object { Split-Path $_ -Leaf } | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $(Split-Path $_ -Leaf)"
}
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))
$publishedAssets += $checksumPath

& (Join-Path $root "scripts\security\audit-release.ps1") `
    -FixtureMode `
    -ArtifactPaths @($publishedAssets + $unpackedByArchitecture.Values)

[pscustomobject]@{
    Assets = @($publishedAssets | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
        [pscustomobject]@{
            File = $_
            Sha256 = $hash.Hash
        }
    })
    Authenticode = if ($invalidSignatures.Count -eq 0) { "Valid" } else { "NotSigned" }
    SignatureFiles = $signatureResults
    ReleaseReady = ($invalidSignatures.Count -eq 0)
    StageRoot = $StageRoot
    Published = $false
} | ConvertTo-Json -Depth 5
