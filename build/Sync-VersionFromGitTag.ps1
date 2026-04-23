param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir,

    [Parameter(Mandatory = $true)]
    [string]$ManifestTemplatePath,

    [Parameter(Mandatory = $true)]
    [string]$ManifestOutputPath,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyInfoTemplatePath,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyInfoOutputPath,

    [string]$GameVersion
)

$ErrorActionPreference = "Stop"

function Get-HighestVersionTag {
    $tags = git -C $ProjectDir tag --sort=-v:refname
    foreach ($tag in $tags) {
        if ($tag -match '^v?(\d+\.\d+\.\d+)$') {
            return $Matches[1]
        }
    }

    throw "No semantic version tag found. Expected tag like v0.0.1."
}

function Resolve-GameVersion {
    param(
        [string]$ExplicitGameVersion
    )

    if ($ExplicitGameVersion -match '^\d+\.\d+\.\d+$') {
        return $ExplicitGameVersion
    }

    $envGameVersion = $env:BSSIDECAR_GAME_VERSION
    if ($envGameVersion -match '^\d+\.\d+\.\d+$') {
        return $envGameVersion
    }

    $branch = git -C $ProjectDir branch --show-current
    if ($branch -match '^\d+\.\d+\.\d+$') {
        return $branch
    }

    $templateJson = Get-Content -Raw $ManifestTemplatePath | ConvertFrom-Json
    if ($templateJson.gameVersion -match '^\d+\.\d+\.\d+$') {
        return $templateJson.gameVersion
    }

    throw "Unable to resolve Beat Saber version from parameter, environment, branch, or template."
}

function Set-FileContentIfChanged {
    param(
        [string]$Path,
        [string]$Content
    )

    $existing = if (Test-Path $Path) { Get-Content -Raw $Path } else { $null }
    if ($existing -ceq $Content) {
        return
    }

    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

$version = Get-HighestVersionTag
$resolvedGameVersion = Resolve-GameVersion -ExplicitGameVersion $GameVersion

$manifestTemplate = Get-Content -Raw $ManifestTemplatePath
$manifestContent = $manifestTemplate.Replace('__VERSION__', $version).Replace('__GAME_VERSION__', $resolvedGameVersion)

$assemblyTemplate = Get-Content -Raw $AssemblyInfoTemplatePath
$assemblyContent = $assemblyTemplate.Replace('__VERSION__', $version)

Set-FileContentIfChanged -Path $ManifestOutputPath -Content $manifestContent
Set-FileContentIfChanged -Path $AssemblyInfoOutputPath -Content $assemblyContent
