<#
Install Raven for this Windows user. No administrator rights are needed.

    Right-click install.ps1 > Run with PowerShell
    or, in PowerShell in this folder:  powershell -ExecutionPolicy Bypass -File .\install.ps1

What it does, and nothing else:
  1. Copies the Raven folder that is next to this script to %LOCALAPPDATA%\Programs\Raven
     (replacing an earlier Raven there).
  2. Puts uninstall.ps1 and INSTALL-Windows.md in that folder.
  3. Adds Raven to your Start menu (%APPDATA%\Microsoft\Windows\Start Menu\Programs\Raven.lnk).
No registry entries, no service, no firewall rule, nothing for other users of this PC. It never
touches your Raven data (%USERPROFILE%\.raven) or this app's settings (%LOCALAPPDATA%\Raven).

-NoPause: don't wait for Enter at the end (for use from another script).
#>
param([switch]$NoPause)

$ErrorActionPreference = 'Stop'

function Finish([int]$code) {
    if (-not $NoPause) {
        Write-Host ''
        [void](Read-Host 'Press Enter to close this window')
    }
    exit $code
}

function Fail([string]$message) {
    Write-Host ''
    Write-Host $message -ForegroundColor Red
    Finish 1
}

# Raven, raven-node or raven-tor-helper running from $folder.
function Get-RavenProcesses([string]$folder) {
    $prefix = $folder.TrimEnd('\') + '\'
    $found = @()
    foreach ($p in @(Get-Process -Name 'Raven', 'raven-node', 'raven-tor-helper' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { }
        if ($path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $found += $p }
    }
    return $found
}

try {
    $here = $PSScriptRoot
    if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $source = Join-Path $here 'Raven'
    $sourceExe = Join-Path $source 'Raven.exe'
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
        Fail ("The Raven folder is not next to install.ps1. Unzip the whole download first " +
            "(right-click the zip > Extract All), then run install.ps1 from the unzipped folder.")
    }
    if (-not [Environment]::Is64BitOperatingSystem) { Fail 'Raven needs 64-bit Windows.' }
    if ([Environment]::OSVersion.Version.Build -lt 17763) {
        Fail 'Raven needs Windows 10 version 1809 or later, or Windows 11.'
    }
    if (-not $env:LOCALAPPDATA) { Fail 'LOCALAPPDATA is not set, so there is no place to install Raven for you.' }

    $version = ''
    try { $version = ((Get-Item -LiteralPath $sourceExe).VersionInfo.ProductVersion -split '\+')[0] } catch { }
    $programs = Join-Path $env:LOCALAPPDATA 'Programs'
    $dest = Join-Path $programs 'Raven'
    $new = "$dest.new"
    $old = "$dest.old"
    Write-Host "Installing Raven $version for $env:USERNAME"
    Write-Host "  into $dest"

    # Never replace a folder that isn't Raven for Windows.
    if (Test-Path -LiteralPath $dest) {
        if (-not (Test-Path -LiteralPath (Join-Path $dest 'raven_client.dll')) -or
            -not (Test-Path -LiteralPath (Join-Path $dest 'Raven.exe'))) {
            Fail "$dest already exists and is not Raven for Windows. Move it somewhere else, then run install.ps1 again."
        }
    }
    $running = @(Get-RavenProcesses $dest)
    if ($running.Count -gt 0) {
        Fail ("Raven is running from $dest. Quit it first: right-click the Raven icon in the notification area " +
            "(near the clock) > Quit and stop the background program. Then run install.ps1 again.")
    }

    # Copy to Raven.new, then swap it in, so a failed copy never leaves half a Raven behind.
    foreach ($p in @($new, $old)) {
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
    }
    New-Item -ItemType Directory -Force -Path $programs | Out-Null
    Write-Host '  copying...'
    Copy-Item -LiteralPath $source -Destination $new -Recurse -Force
    foreach ($f in @('uninstall.ps1', 'INSTALL-Windows.md')) {
        $file = Join-Path $here $f
        if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination $new -Force }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $new 'Raven.exe'))) { Fail "The copy to $new is incomplete." }
    if (Test-Path -LiteralPath $dest) {
        try {
            Rename-Item -LiteralPath $dest -NewName (Split-Path -Leaf $old)
        } catch {
            Remove-Item -LiteralPath $new -Recurse -Force -ErrorAction SilentlyContinue
            Fail "Could not replace the Raven in $dest (is a file in it still open?): $($_.Exception.Message)"
        }
    }
    try {
        Rename-Item -LiteralPath $new -NewName (Split-Path -Leaf $dest)
    } catch {
        if (Test-Path -LiteralPath $old) { Rename-Item -LiteralPath $old -NewName (Split-Path -Leaf $dest) -ErrorAction SilentlyContinue }
        throw
    }
    if (Test-Path -LiteralPath $old) {
        try { Remove-Item -LiteralPath $old -Recurse -Force } catch {
            Write-Host "  (could not remove the previous copy in $old; you can delete that folder later)"
        }
    }

    # The Start menu entry.
    $exe = Join-Path $dest 'Raven.exe'
    $startMenu = [Environment]::GetFolderPath('Programs')
    $shortcut = Join-Path $startMenu 'Raven.lnk'
    try {
        $shell = New-Object -ComObject WScript.Shell
        $lnk = $shell.CreateShortcut($shortcut)
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = $dest
        $lnk.IconLocation = "$exe,0"
        $lnk.Description = 'Raven, a private messenger with no central server'
        $lnk.Save()
        $menu = 'Start menu: Raven'
    } catch {
        $menu = "Start menu: not added ($($_.Exception.Message)). Start Raven from $exe"
    }

    Write-Host ''
    Write-Host 'Raven is installed.' -ForegroundColor Green
    Write-Host "  App:        $dest"
    Write-Host "  $menu"
    Write-Host ''
    Write-Host 'Next: open Raven from the Start menu.'
    Write-Host ' - The first time, Windows SmartScreen may say "Windows protected your PC": click More info,'
    Write-Host '   then Run anyway. Raven is not signed, so Windows does not know it yet.'
    Write-Host ' - If Windows Firewall asks about raven-node: tick Private networks only, then Allow access.'
    Write-Host 'INSTALL-Windows.md (also in the folder above) explains each step.'
    Write-Host "To remove Raven: run uninstall.ps1 in $dest"
    Finish 0
} catch {
    Fail ("Raven was not installed: " + $_.Exception.Message)
}
