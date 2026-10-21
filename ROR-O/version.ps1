# Reads manifest.json version_number and stamps it into a copy of the manifest.
# Prints the version on stdout. Build-time only; do not ship this script in the mod zip.
param(
    [Parameter(Mandatory = $true)][string]$Manifest,
    [string]$Version = "",
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"

$manifestText = [IO.File]::ReadAllText($Manifest)
if (-not $Version) {
    $match = [regex]::Match($manifestText, '"version_number"\s*:\s*"([0-9]+\.[0-9]+\.[0-9]+)"')
    if (-not $match.Success) {
        throw "manifest.json has no three-part version_number."
    }
    $Version = $match.Groups[1].Value
}

if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "ModVersion must be a three-part version. Got '$Version'."
}

if ($Destination) {
    $updated = [regex]::Replace(
        $manifestText,
        '"version_number"\s*:\s*"[0-9]+\.[0-9]+\.[0-9]+"',
        ('"version_number": "' + $Version + '"'))
    $directory = Split-Path -Parent $Destination
    if ($directory) { [void][IO.Directory]::CreateDirectory($directory) }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [IO.File]::WriteAllText($Destination, $updated, $utf8)
}

[Console]::Out.Write($Version)
