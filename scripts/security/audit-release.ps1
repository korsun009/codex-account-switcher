[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string[]]$ArtifactPaths = @(),
    [string]$GitleaksPath,
    [switch]$FixtureMode
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

$gitleaksVersion = '8.30.1'
$gitleaksArchiveSha256 = 'D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E'
$sensitiveNamePattern = '(?i)(^|/)(auth\.json|\.env$|\.env\.(?!example$).*|switcher\.db|.*\.pem|.*\.pfx|id_(?:rsa|ecdsa|ed25519)|_account_profiles(?:/|$)|_account_switcher_backups(?:/|$))'

function Assert-NoSensitiveNames {
    param([string[]]$Names, [string]$Scope)

    $matches = @($Names | ForEach-Object { $_.Replace('\', '/') } | Where-Object { $_ -match $sensitiveNamePattern })
    if ($matches.Count -gt 0) {
        $safeList = $matches | Sort-Object -Unique
        throw "Sensitive file-name policy failed for ${Scope}: $($safeList -join ', ')"
    }
}

function Resolve-Gitleaks {
    if ($GitleaksPath) {
        return (Resolve-Path -LiteralPath $GitleaksPath).Path
    }

    $toolRoot = Join-Path ([IO.Path]::GetTempPath()) "codex-account-switcher-tools\gitleaks-$gitleaksVersion"
    $executable = Join-Path $toolRoot 'gitleaks.exe'
    if (Test-Path -LiteralPath $executable) {
        return $executable
    }

    New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
    $archive = Join-Path $toolRoot "gitleaks_${gitleaksVersion}_windows_x64.zip"
    $uri = "https://github.com/gitleaks/gitleaks/releases/download/v${gitleaksVersion}/gitleaks_${gitleaksVersion}_windows_x64.zip"
    Invoke-WebRequest -Uri $uri -OutFile $archive -UseBasicParsing
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash
    if ($actualHash -ne $gitleaksArchiveSha256) {
        throw 'Pinned Gitleaks archive checksum mismatch.'
    }

    Expand-Archive -LiteralPath $archive -DestinationPath $toolRoot -Force
    return (Resolve-Path -LiteralPath $executable).Path
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) {
    git -C $root rev-parse --is-inside-work-tree 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Not a Git worktree: $root"
    }
}

$tracked = @(git -C $root ls-files)
if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
Assert-NoSensitiveNames -Names $tracked -Scope 'tracked files'

$currentSource = @(git -C $root ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'git current source inventory failed.' }
Assert-NoSensitiveNames -Names $currentSource -Scope 'current source files'

$historyNames = @(git -C $root log --all --name-only --pretty=format: | Where-Object { $_ })
if ($LASTEXITCODE -ne 0) { throw 'git history name scan failed.' }
Assert-NoSensitiveNames -Names $historyNames -Scope 'Git history'

if ($FixtureMode) {
    $fixtureRejected = $false
    try {
        Assert-NoSensitiveNames -Names @('safe/readme.md', 'fixture/auth.json') -Scope 'synthetic fixture'
    }
    catch {
        $fixtureRejected = $true
    }
    if (-not $fixtureRejected) {
        throw 'Synthetic sensitive-name fixture was not rejected.'
    }
    Assert-NoSensitiveNames -Names @('integration/.env.example') -Scope 'safe example fixture'
}

$gitleaks = Resolve-Gitleaks
& $gitleaks git --redact --no-banner --exit-code 1 $root
if ($LASTEXITCODE -ne 0) {
    throw 'Gitleaks found a potential secret in Git history.'
}

$currentSnapshot = Join-Path ([IO.Path]::GetTempPath()) ("codex-account-switcher-current-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $currentSnapshot -Force | Out-Null
try {
    foreach ($relativePath in $currentSource) {
        $sourcePath = Join-Path $root $relativePath
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            continue
        }
        $targetPath = Join-Path $currentSnapshot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    }
    & $gitleaks dir --redact --no-banner --exit-code 1 $currentSnapshot
    if ($LASTEXITCODE -ne 0) {
        throw 'Gitleaks found a potential secret in the current source tree.'
    }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedSnapshot = [IO.Path]::GetFullPath($currentSnapshot)
    if (-not $resolvedSnapshot.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove non-temporary snapshot: $resolvedSnapshot"
    }
    Remove-Item -LiteralPath $resolvedSnapshot -Recurse -Force -ErrorAction SilentlyContinue
}

foreach ($artifactPath in $ArtifactPaths) {
    $resolvedArtifact = (Resolve-Path -LiteralPath $artifactPath).Path
    if (Test-Path -LiteralPath $resolvedArtifact -PathType Leaf) {
        $artifactNames = @((Split-Path -Leaf $resolvedArtifact))
    } else {
        $artifactPrefix = $resolvedArtifact.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $artifactNames = @(Get-ChildItem -LiteralPath $resolvedArtifact -Recurse -File | ForEach-Object {
            $_.FullName.Substring($artifactPrefix.Length)
        })
    }
    Assert-NoSensitiveNames -Names $artifactNames -Scope "artifact $resolvedArtifact"
    & $gitleaks dir --redact --no-banner --exit-code 1 $resolvedArtifact
    if ($LASTEXITCODE -ne 0) {
        throw "Gitleaks found a potential secret in artifact $resolvedArtifact."
    }
}

Write-Host 'Secret audit passed: Git history, current source, and requested artifacts contain no detected credentials.'
