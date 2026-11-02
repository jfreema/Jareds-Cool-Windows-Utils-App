<#
.SYNOPSIS
Builds a version of the app for release, as the workflows do: the installer, the portable exe, and release.json, which
Check for Updates reads.

.DESCRIPTION
Writes to -Out:
  app\                           what the installer installs: the app with its own copy of .NET
  JaredsWindowsUtils-Setup.exe   the installer
  JaredsWindowsUtils.exe         the portable app, all in one file
  release.json                   the version, and where the two downloads are with their SHA-256 hashes
Needs Windows, the .NET 10 SDK and Inno Setup 6, which it installs with Chocolatey if it's missing.

.EXAMPLE
scripts/build.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Out = "out",
    # Where release.json says the downloads are: the GitHub release for this version, unless testing
    [string] $DownloadUrl = "https://github.com/jfreema/Jareds-Cool-Windows-Utils-App/releases/download/v$Version"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot
$project = Join-Path $root "src\JaredsWindowsUtils\JaredsWindowsUtils.csproj"
$Out = [IO.Path]::GetFullPath($Out, (Get-Location).Path)

function Find-Iscc {
    $patterns = @("${env:ProgramFiles(x86)}\Inno Setup *\ISCC.exe", "$env:ProgramFiles\Inno Setup *\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup *\ISCC.exe")
    $find = { $patterns | ForEach-Object { Get-Item $_ -ErrorAction SilentlyContinue } | Select-Object -First 1 }
    $found = & $find
    # The installer script needs 6.3 or later, for x64compatible
    $version = $null
    if ($found -and -not [version]::TryParse($found.VersionInfo.FileVersion, [ref] $version)) { $version = $null }
    if (-not $found -or ($version -and $version -lt [version] "6.3")) {
        Write-Host "Installing the newest Inno Setup"
        choco upgrade innosetup --no-progress -y | Out-Host
        $found = & $find
    }
    if (-not $found) { throw "Inno Setup isn't installed. Get it from https://jrsoftware.org/isdl.php" }
    Write-Host "Inno Setup $($found.VersionInfo.FileVersion)"
    return $found.FullName
}

function Publish([string] $folder, [string[]] $options) {
    dotnet publish $project -c Release -r win-x64 --self-contained -p:Version=$Version -o $folder @options
    if ($LASTEXITCODE) { throw "dotnet publish failed" }
    Get-ChildItem $folder -Filter *.pdb | Remove-Item
}

function Get-Sha256([string] $path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory $Out | Out-Null

Publish "$Out\app" @()
Publish "$Out\portable" @("-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true")
Move-Item "$Out\portable\JaredsWindowsUtils.exe" "$Out\JaredsWindowsUtils.exe"
Remove-Item -Recurse "$Out\portable"

$iscc = Find-Iscc
& $iscc "/DAppVersion=$Version" "/DSourceDir=$Out\app" "/DOutputDir=$Out" (Join-Path $root "installer\JaredsWindowsUtils.iss")
if ($LASTEXITCODE) { throw "Inno Setup failed" }

$release = [ordered]@{
    version   = $Version
    installer = [ordered]@{ url = "$DownloadUrl/JaredsWindowsUtils-Setup.exe"; sha256 = Get-Sha256 "$Out\JaredsWindowsUtils-Setup.exe" }
    portable  = [ordered]@{ url = "$DownloadUrl/JaredsWindowsUtils.exe"; sha256 = Get-Sha256 "$Out\JaredsWindowsUtils.exe" }
}
$release | ConvertTo-Json | Set-Content "$Out\release.json" -Encoding utf8NoBOM

foreach ($file in "JaredsWindowsUtils-Setup.exe", "JaredsWindowsUtils.exe") {
    $item = Get-Item "$Out\$file"
    # Inno Setup pads the installer's with spaces
    $stamped = "$($item.VersionInfo.ProductVersion)".Trim()
    if ($stamped -ne $Version) { throw "$file says it's version $stamped, not $Version" }
    Write-Host ("{0} {1}: {2:N1} MB" -f $file, $stamped, ($item.Length / 1MB))
}
