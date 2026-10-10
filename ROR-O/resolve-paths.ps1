# Resolves the Risk of Rain 2 install and a mod-manager profile for ROR-O.csproj.
# Prints nothing on success except by writing -OutFile. Diagnostics go to stderr.
param(
    [string]$GameDir = "",
    [string]$ProfileDir = "",
    [string]$ProfileRoot = "",
    [string]$OutFile = "",
    [switch]$Ask
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$Message) {
    [Console]::Error.WriteLine($Message)
}

function New-PathSet {
    return New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
}

function Add-Path([System.Collections.Generic.HashSet[string]]$Set, [string]$Path) {
    if (-not $Path) { return }
    try { $Path = [IO.Path]::GetFullPath($Path) } catch { return }
    if (Test-Path $Path) { [void]$Set.Add($Path) }
}

function Get-SteamLibraries {
    $libraries = New-PathSet
    foreach ($key in @(
            "HKCU:\Software\Valve\Steam",
            "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
            "HKLM:\SOFTWARE\Valve\Steam")) {
        if (-not (Test-Path $key)) { continue }
        $props = Get-ItemProperty $key
        foreach ($name in @("SteamPath", "InstallPath")) {
            Add-Path $libraries $props.$name
        }
    }

    foreach ($root in @($libraries)) {
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (-not (Test-Path $vdf)) { $vdf = Join-Path $root "config\libraryfolders.vdf" }
        if (-not (Test-Path $vdf)) { continue }
        $text = [IO.File]::ReadAllText($vdf)
        foreach ($match in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
            Add-Path $libraries ($match.Groups[1].Value.Replace("\\", "\"))
        }
    }
    return $libraries
}

function Get-GameInstalls {
    $found = New-PathSet
    foreach ($library in Get-SteamLibraries) {
        $candidate = Join-Path $library "steamapps\common\Risk of Rain 2"
        $managed = Join-Path $candidate "Risk of Rain 2_Data\Managed\RoR2.dll"
        if (Test-Path $managed) { [void]$found.Add([IO.Path]::GetFullPath($candidate)) }
    }
    return $found
}

function Add-ProfileRoot([System.Collections.Generic.HashSet[string]]$Roots, [string]$Path) {
    Add-Path $Roots $Path
}

function Import-SavedChoice {
    foreach ($path in @(
            (Join-Path $PSScriptRoot "roro.user.props"),
            (Join-Path $PSScriptRoot "obj\roro.paths.props"))) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        try { [xml]$xml = Get-Content -LiteralPath $path -Raw } catch { continue }
        foreach ($name in @("GameDir", "ProfileDir", "ProfileRoot")) {
            $node = $xml.SelectSingleNode("//$name")
            if (-not $node) { continue }
            $value = $node.InnerText.Trim()
            if (-not $value) { continue }
            if (-not (Get-Variable -Name $name -Scope Script -ValueOnly)) {
                Set-Variable -Name $name -Scope Script -Value $value
            }
        }
    }
}

function Test-GameDir([string]$Path) {
    return [bool]($Path -and (Test-Path -LiteralPath (Join-Path $Path "Risk of Rain 2_Data\Managed\RoR2.dll")))
}

function Test-ProfileDir([string]$Path) {
    return [bool]($Path -and (Test-Path -LiteralPath (Join-Path $Path "BepInEx\core\BepInEx.dll")))
}

function Get-ProfileRoots {
    $roots = New-PathSet
    $appData = $env:APPDATA
    $local = $env:LOCALAPPDATA
    if ($ProfileRoot) { Add-ProfileRoot $roots $ProfileRoot }
    foreach ($relative in @(
            "r2modmanPlus-local\RiskOfRain2\profiles",
            "r2modmanPlus\RiskOfRain2\profiles",
            "Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles",
            "Thunderstore Mod Manager\RiskOfRain2\profiles")) {
        Add-ProfileRoot $roots (Join-Path $appData $relative)
        Add-ProfileRoot $roots (Join-Path $local $relative)
    }

    $configDirs = @(
        (Join-Path $appData "r2modmanPlus-local"),
        (Join-Path $appData "r2modmanPlus"),
        (Join-Path $appData "Thunderstore Mod Manager"),
        (Join-Path $appData "com.kesomannen.gale")
    )
    foreach ($dir in $configDirs) {
        if (-not (Test-Path $dir)) { continue }
        Get-ChildItem $dir -Recurse -Include *.json, *.yml -Depth 3 -ErrorAction SilentlyContinue | ForEach-Object {
            $text = ""
            try { $text = [IO.File]::ReadAllText($_.FullName) } catch { return }
            foreach ($match in [regex]::Matches($text, '[A-Za-z]:\\(?:[^"\r\n<>|*?]+\\)*[^"\r\n<>|*?]+')) {
                $path = $match.Value.TrimEnd('\')
                if ($path -match '\\profiles$') { Add-ProfileRoot $roots $path }
                else { Add-ProfileRoot $roots (Join-Path $path "profiles") }
            }
        }
    }
    return $roots
}

function Get-Profiles {
    $profiles = New-PathSet
    foreach ($root in Get-ProfileRoots) {
        Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $bep = Join-Path $_.FullName "BepInEx\core\BepInEx.dll"
            if (Test-Path $bep) { [void]$profiles.Add($_.FullName) }
        }
    }
    return $profiles
}

function Resolve-One([string]$Label, [string]$Requested, $Options, [scriptblock]$Validate) {
    $Options = @($Options)
    if ($Requested) {
        $direct = $Requested
        if (-not [IO.Path]::IsPathRooted($direct)) {
            $named = @($Options | Where-Object { [IO.Path]::GetFileName($_) -eq $Requested })
            if ($named.Count -eq 1) { return $named[0] }
            Write-Info "$Label '$Requested' is not a full path and does not match one discovered folder."
            $Options | ForEach-Object { Write-Info "  $_" }
            exit 2
        }
        if (-not (& $Validate $direct)) {
            Write-Info "$Label is not usable: $direct"
            exit 2
        }
        return [IO.Path]::GetFullPath($direct)
    }

    if ($Options.Count -eq 1) { return $Options[0] }
    if ($Options.Count -eq 0) {
        Write-Info "No $Label was found."
        Write-Info "Pass -p:$($Label)=`"full path`" or put it in ROR-O/roro.user.props (see roro.user.props.example)."
        if ($Label -eq "ProfileDir") {
            Write-Info "If the mod manager keeps profiles outside AppData, set ProfileRoot to the folder that contains them."
        }
        exit 2
    }

    Write-Info "Several $($Label)s were found:"
    for ($i = 0; $i -lt $Options.Count; $i++) {
        Write-Info ("  {0}. {1}" -f ($i + 1), $Options[$i])
    }

    $canAsk = $Ask -or (-not [Console]::IsInputRedirected)
    if (-not $canAsk) {
        Write-Info "Rebuild with -p:$($Label)=`"one of the paths above`", or run:"
        Write-Info "  powershell -File ROR-O\resolve-paths.ps1 -Ask"
        exit 2
    }

    $answer = Read-Host "Number of the $Label to use"
    $index = 0
    if (-not [int]::TryParse($answer, [ref]$index) -or $index -lt 1 -or $index -gt $Options.Count) {
        Write-Info "That was not a number from the list."
        exit 2
    }
    return $Options[$index - 1]
}

function Find-LookingGlass([string]$Profile) {
    $plugins = Join-Path $Profile "BepInEx\plugins"
    if (-not (Test-Path $plugins)) { return "" }
    $hit = Get-ChildItem $plugins -Recurse -Filter "LookingGlass.dll" -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "LookingGlass" } |
        Select-Object -First 1
    if (-not $hit) {
        $hit = Get-ChildItem $plugins -Recurse -Filter "LookingGlass.dll" -ErrorAction SilentlyContinue |
            Select-Object -First 1
    }
    if ($hit) { return $hit.FullName }
    return ""
}

function Find-DeployDir([string]$Profile) {
    $plugins = Join-Path $Profile "BepInEx\plugins"
    if (Test-Path $plugins) {
        $existing = Get-ChildItem $plugins -Recurse -Filter "ROR-O.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($existing) { return $existing.Directory.FullName }
    }
    return (Join-Path $plugins "ROR-O")
}

function Save-UserProps([string]$Game, [string]$Profile) {
    $propsPath = Join-Path $PSScriptRoot "roro.user.props"
    $root = Split-Path -Parent $Profile
    $xml = @"
<Project>
  <PropertyGroup>
    <GameDir>$([System.Security.SecurityElement]::Escape($Game))</GameDir>
    <ProfileRoot>$([System.Security.SecurityElement]::Escape($root))</ProfileRoot>
    <ProfileDir>$([System.Security.SecurityElement]::Escape($Profile))</ProfileDir>
  </PropertyGroup>
</Project>
"@
    [IO.File]::WriteAllText($propsPath, $xml.Trim(), [Text.UTF8Encoding]::new($false))
    Write-Info "Saved $propsPath"
    Write-Info "That file is gitignored. Later builds on this machine reuse it."
}

Import-SavedChoice
$rememberedProfile = $ProfileDir
if ($Ask) { $ProfileDir = "" }
if (-not $ProfileRoot -and $rememberedProfile) {
    $parent = Split-Path -Parent $rememberedProfile
    if ($parent -and (Test-Path -LiteralPath $parent)) { $ProfileRoot = $parent }
}

if (-not $Ask -and (Test-GameDir $GameDir) -and (Test-ProfileDir $ProfileDir)) {
    $game = [IO.Path]::GetFullPath($GameDir)
    $profile = [IO.Path]::GetFullPath($ProfileDir)
}
else {
    $games = @(Get-GameInstalls)
    $profiles = @(Get-Profiles)
    $game = Resolve-One "GameDir" $GameDir $games { param($path) Test-GameDir $path }
    $profile = Resolve-One "ProfileDir" $ProfileDir $profiles { param($path) Test-ProfileDir $path }
}
$looking = Find-LookingGlass $profile
if (-not $looking) {
    Write-Info "LookingGlass.dll was not found under $profile\BepInEx\plugins."
    Write-Info "Install Looking Glass in that profile, or the project cannot reference it."
    exit 2
}
$deploy = Find-DeployDir $profile

Save-UserProps $game $profile

Write-Info "Game: $game"
Write-Info "Profile: $profile"
Write-Info "Deploy: $deploy"

if ($OutFile) {
    $directory = Split-Path $OutFile -Parent
    if ($directory) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
    $xml = @"
<Project>
  <PropertyGroup>
    <GameDir>$([System.Security.SecurityElement]::Escape($game))</GameDir>
    <ProfileDir>$([System.Security.SecurityElement]::Escape($profile))</ProfileDir>
    <LookingGlassDll>$([System.Security.SecurityElement]::Escape($looking))</LookingGlassDll>
    <DeployPluginDir>$([System.Security.SecurityElement]::Escape($deploy))</DeployPluginDir>
  </PropertyGroup>
</Project>
"@
    [IO.File]::WriteAllText($OutFile, $xml.Trim(), [Text.UTF8Encoding]::new($false))
}
