[CmdletBinding()]
param(
    [Parameter(Mandatory, ValueFromRemainingArguments)]
    [string[]]$GhArguments
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
$gh = if ($ghCommand) { $ghCommand.Source } else { Join-Path $env:ProgramFiles "GitHub CLI\gh.exe" }
if (-not (Test-Path -LiteralPath $gh)) {
    throw "GitHub CLI was not found. Install the official GitHub CLI first."
}
$git = (Get-Command git -ErrorAction Stop).Source
$credentialRequest = "protocol=https`nhost=github.com`n`n"
$credentialLines = @($credentialRequest | & $git credential fill)
if ($LASTEXITCODE -ne 0) {
    throw "Git Credential Manager did not return a GitHub credential."
}

$passwordLine = $credentialLines | Where-Object { $_ -like "password=*" } | Select-Object -First 1
if (-not $passwordLine) {
    throw "The stored GitHub credential has no access token."
}
$token = $passwordLine.Substring("password=".Length)
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "The stored GitHub access token is empty."
}

$previousToken = $env:GH_TOKEN
try {
    $env:GH_TOKEN = $token
    & $gh @GhArguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed with exit code $LASTEXITCODE."
    }
} finally {
    $env:GH_TOKEN = $previousToken
    $token = $null
    $credentialLines = $null
}
