[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86')]
    [string[]]$Architecture = @('x64', 'x86'),
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repoRoot 'control-deck\CodexControlDeck\CodexControlDeck.csproj'
$msiProject = Join-Path $PSScriptRoot 'msi\CodexControlDeck.Msi.wixproj'
$bundleProject = Join-Path $PSScriptRoot 'bootstrapper\CodexControlDeck.Bundle.wixproj'
$artifactRoot = Join-Path $repoRoot 'artifacts\control-deck'
$logRoot = Join-Path $artifactRoot 'logs'

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

foreach ($arch in $Architecture) {
    $rid = "win-$arch"
    $publishDir = Join-Path $artifactRoot "publish\$rid"
    $internalDir = Join-Path $artifactRoot "internal\$rid"
    $installerDir = Join-Path $repoRoot "artifacts\installers\$rid"
    $symbolsDir = Join-Path $artifactRoot "symbols\$rid"

    New-Item -ItemType Directory -Force -Path $publishDir, $internalDir, $installerDir, $symbolsDir | Out-Null

    & dotnet publish $project -c Release -r $rid -p:Platform=$arch --self-contained true `
        -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o $publishDir --nologo `
        *> (Join-Path $logRoot "publish-$rid.log")
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid with exit code $LASTEXITCODE." }

    foreach ($required in @('CodexControlDeck.exe', 'CodexControlDeck.pri', 'App.xbf', 'MainWindow.xbf', 'MainPage.xbf', 'Assets\AppIcon.ico')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) {
            throw "Published $rid build is missing $required."
        }
    }

    & dotnet build $msiProject -c Release -p:Platform=$arch -p:ProductVersion=$Version `
        -p:PublishDir=$publishDir -p:OutputPath=$internalDir --nologo `
        *> (Join-Path $logRoot "msi-$rid.log")
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed for $rid with exit code $LASTEXITCODE." }

    $msi = Join-Path $internalDir "Codex-Control-Deck-$Version-$arch.msi"
    if (-not (Test-Path -LiteralPath $msi)) { throw "Expected MSI was not created: $msi" }

    & wix msi validate -sice ICE03 $msi *> (Join-Path $logRoot "msi-validate-$rid.log")
    if ($LASTEXITCODE -ne 0) { throw "MSI validation failed for $rid with exit code $LASTEXITCODE." }

    & dotnet build $bundleProject -c Release -p:Platform=$arch -p:ProductVersion=$Version `
        -p:MsiPath=$msi -p:PublishDir=$publishDir -p:OutputPath=$installerDir --nologo `
        *> (Join-Path $logRoot "bundle-$rid.log")
    if ($LASTEXITCODE -ne 0) { throw "Bundle build failed for $rid with exit code $LASTEXITCODE." }

    $bundle = Join-Path $installerDir "Codex-Control-Deck-$Version.exe"
    if (-not (Test-Path -LiteralPath $bundle)) { throw "Expected bundle was not created: $bundle" }

    Get-ChildItem -LiteralPath $internalDir, $installerDir -Filter '*.wixpdb' -File |
        Move-Item -Destination $symbolsDir -Force
}

Write-Output "Built Codex Control Deck $Version installers for: $($Architecture -join ', ')."
