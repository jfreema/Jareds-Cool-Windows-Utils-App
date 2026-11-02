<#
.SYNOPSIS
Tries the built app on this Windows machine, as the workflows do on GitHub's runners before every release.

.DESCRIPTION
Run scripts/build.ps1 first, twice: for the version to try, into -Out, and as 99.0.0 into -Newer with
-DownloadUrl http://127.0.0.1:8765, for Check for Updates to find. Then this:
  1. runs the self-test (JaredsWindowsUtils.exe --self-test): pictures of the menu and dialogs, and a test window
     snapped around the screen
  2. installs the version to try, points its Check for Updates at the 99.0.0 build served from this machine, and
     checks that it updates itself and restarts on the new version
  3. does the same for a portable copy
  4. checks the uninstaller removes the app and Open at Login
Writes what it found to -Results (results.json, the self-test's pictures and the app's logs), and exits 1 if
anything failed. It changes this machine's copy of the app and its settings, so it's for a test machine.

.EXAMPLE
scripts/build.ps1 -Version 0.1.0
scripts/build.ps1 -Version 99.0.0 -Out out-newer -DownloadUrl http://127.0.0.1:8765
scripts/try-on-windows.ps1
#>
param(
    [string] $Out = "out",
    [string] $Newer = "out-newer",
    [string] $Results = "trial"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$Out = [IO.Path]::GetFullPath($Out, (Get-Location).Path)
$Newer = [IO.Path]::GetFullPath($Newer, (Get-Location).Path)
$Results = [IO.Path]::GetFullPath($Results, (Get-Location).Path)
if (Test-Path $Results) { Remove-Item -Recurse -Force $Results }
New-Item -ItemType Directory $Results | Out-Null

$version = (Get-Content "$Out\release.json" | ConvertFrom-Json).version
$data = Join-Path $env:LOCALAPPDATA "JaredsWindowsUtils"
$installed = Join-Path $env:LOCALAPPDATA "Programs\JaredsWindowsUtils\JaredsWindowsUtils.exe"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$checks = [Collections.Generic.List[object]]::new()

function Check([string] $name, [bool] $passed, [string] $details = "") {
    $checks.Add([ordered]@{ name = $name; passed = $passed; details = $details })
    Write-Host ("{0} {1} {2}" -f ($passed ? "PASS" : "FAIL"), $name, $details)
}

# True once the condition holds, false if it doesn't within the time
function Wait-Until([int] $seconds, [scriptblock] $condition) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return [bool](& $condition)
}

function Get-Running([string] $path) {
    @(Get-Process JaredsWindowsUtils -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $path })
}

# The version the build stamped on it, or nothing while it's missing, as mid-update
function Get-Stamped([string] $path) {
    $item = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if ($item) { "$($item.VersionInfo.ProductVersion)".Trim() } else { $null }
}

function Save-Log([string] $name) {
    if (Test-Path "$data\log.txt") { Copy-Item "$data\log.txt" (Join-Path $Results "$name-log.txt") }
}

function Stop-Copy([string] $path) {
    $quit = Start-Process $path -ArgumentList "--quit" -PassThru
    $quit.WaitForExit(30000) | Out-Null
    return Wait-Until 10 { @(Get-Running $path).Count -eq 0 }
}

try {
    # 1. The self-test
    $selftest = Join-Path $Results "selftest"
    $test = Start-Process "$Out\JaredsWindowsUtils.exe" -ArgumentList "--self-test", "`"$selftest`"" -PassThru
    if (-not $test.WaitForExit(240000)) {
        $test.Kill()
        Check "self-test finishes" $false "still running after 4 minutes"
    } else {
        Check "self-test passes" ($test.ExitCode -eq 0) "exit code $($test.ExitCode), see selftest/report.json"
    }
    Get-Process JaredsWindowsUtils -ErrorAction SilentlyContinue | Stop-Process -Force

    # Check for Updates finds the newer build here
    $feed = Join-Path ([IO.Path]::GetTempPath()) "jaredswindowsutils-feed"
    if (Test-Path $feed) { Remove-Item -Recurse -Force $feed }
    New-Item -ItemType Directory $feed | Out-Null
    Copy-Item "$Newer\JaredsWindowsUtils-Setup.exe", "$Newer\JaredsWindowsUtils.exe", "$Newer\release.json" $feed
    $server = Start-Process python -ArgumentList "-m", "http.server", "8765", "--bind", "127.0.0.1", "--directory",
        "`"$feed`"" -PassThru -WindowStyle Hidden
    $served = Wait-Until 20 {
        try { (Invoke-WebRequest "http://127.0.0.1:8765/release.json" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 }
        catch { $false }
    }
    Check "newer build served" $served
    New-Item -ItemType Directory -Force $data | Out-Null
    @{ updateFeed = "http://127.0.0.1:8765/release.json" } | ConvertTo-Json |
        Set-Content "$data\settings.json" -Encoding utf8NoBOM
    if (Test-Path "$data\log.txt") { Remove-Item "$data\log.txt" }

    # 2. An installed copy
    $setup = Start-Process "$Out\JaredsWindowsUtils-Setup.exe" -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES",
        "/NORESTART", "/SP-" -PassThru
    $setup.WaitForExit(180000) | Out-Null
    Check "installer finishes" ($setup.HasExited -and $setup.ExitCode -eq 0) "exit code $($setup.ExitCode)"
    Check "installs $version" ((Test-Path $installed) -and (Get-Stamped $installed) -eq $version) $installed
    $shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Jared's Windows App.lnk"
    Check "Start menu shortcut" (Test-Path -LiteralPath $shortcut) $shortcut
    Start-Process $installed -ArgumentList "--update-now" | Out-Null
    $updated = Wait-Until 240 { (Get-Stamped $installed) -eq "99.0.0" -and @(Get-Running $installed).Count -eq 1 }
    Check "installed copy updates itself and restarts" $updated "now $(Get-Stamped $installed)"
    Save-Log "installed"
    Check "installed copy quits when asked" (Stop-Copy $installed)

    # 3. A portable copy
    if (Test-Path "$data\log.txt") { Remove-Item "$data\log.txt" }
    $folder = Join-Path ([IO.Path]::GetTempPath()) "jaredswindowsutils-portable"
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
    New-Item -ItemType Directory $folder | Out-Null
    $portable = Join-Path $folder "JaredsWindowsUtils.exe"
    Copy-Item "$Out\JaredsWindowsUtils.exe" $portable
    Start-Process $portable -ArgumentList "--update-now" | Out-Null
    $updated = Wait-Until 240 { (Get-Stamped $portable) -eq "99.0.0" -and @(Get-Running $portable).Count -eq 1 }
    Check "portable copy updates itself and restarts" $updated "now $(Get-Stamped $portable)"
    Check "portable copy removes the old one" (Wait-Until 30 { -not (Test-Path "$portable.old") })
    Save-Log "portable"
    Check "portable copy quits when asked" (Stop-Copy $portable)

    # 4. The uninstaller, with Open at Login on
    Set-ItemProperty $runKey -Name JaredsWindowsUtils -Value "`"$installed`" --login"
    Start-Process $installed | Out-Null
    Wait-Until 20 { @(Get-Running $installed).Count -eq 1 } | Out-Null
    $uninstaller = Join-Path (Split-Path $installed) "unins000.exe"
    Start-Process $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" | Out-Null
    Check "uninstaller removes the app" (Wait-Until 120 { -not (Test-Path $installed) })
    Check "uninstaller quits the app" (@(Get-Running $installed).Count -eq 0)
    $entry = Get-ItemProperty $runKey -Name JaredsWindowsUtils -ErrorAction SilentlyContinue
    Check "uninstaller removes Open at Login" ($null -eq $entry)
    Check "uninstaller removes the shortcut" (-not (Test-Path -LiteralPath $shortcut))
} catch {
    Check "trial runs to the end" $false ($_ | Out-String)
} finally {
    if (Get-Variable server -ErrorAction SilentlyContinue) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
    Get-Process JaredsWindowsUtils -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

$passed = -not ($checks | Where-Object { -not $_.passed })
[ordered]@{ version = $version; passed = $passed; checks = $checks } | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $Results "results.json") -Encoding utf8NoBOM
if (-not $passed) { exit 1 }
