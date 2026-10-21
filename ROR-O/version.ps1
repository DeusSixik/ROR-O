# Reads mod_version from version.properties and stamps it into a copy of the manifest.
# Prints the version on stdout. Build-time only; do not ship this script in the mod zip.
param(
    [Parameter(Mandatory = $true)][string]$Manifest,
    [Parameter(Mandatory = $true)][string]$VersionFile,
    [string]$Version = "",
    [string]$Destination = ""
)

$ErrorActionPreference = "Stop"

$manifestText = [IO.File]::ReadAllText($Manifest)
if (-not $Version) {
    $propertiesText = [IO.File]::ReadAllText($VersionFile)
    $match = [regex]::Match($propertiesText, '(?m)^\s*mod_version\s*=\s*([^\r\n#]+?)\s*$')
    if (-not $match.Success) {
        throw "$VersionFile has no mod_version property."
    }
    $Version = $match.Groups[1].Value.Trim()
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
