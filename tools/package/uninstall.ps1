<#
Remove Raven for this Windows user (what install.ps1 added). No administrator rights are needed.

    Right-click uninstall.ps1 (in %LOCALAPPDATA%\Programs\Raven) > Run with PowerShell
    or:  powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Raven\uninstall.ps1"

It removes the app folder %LOCALAPPDATA%\Programs\Raven and the Start menu entry, nothing else.
It never deletes your Raven data: your identity, friends and messages stay in %USERPROFILE%\.raven
(or the folder RAVEN_DATA_DIR names), and this app's settings in %LOCALAPPDATA%\Raven. At the end
it names those of them that exist and says how to erase them if you want that too.

-NoPause: don't ask or wait for Enter (for use from another script); while Raven is running it then
stops without removing anything.
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
    if (-not $env:LOCALAPPDATA) { Fail 'LOCALAPPDATA is not set, so Raven cannot be found.' }
    # Leave the folder this script may be in: Windows can't remove a folder that is in use.
    $away = $env:TEMP
    if (-not $away) { $away = $env:USERPROFILE }
    Set-Location -LiteralPath $away
    [System.IO.Directory]::SetCurrentDirectory($away)

    $dest = Join-Path (Join-Path $env:LOCALAPPDATA 'Programs') 'Raven'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Raven.lnk'
    Write-Host "Removing Raven for $env:USERNAME"

    $running = @(Get-RavenProcesses $dest)
    if ($running.Count -gt 0) {
        Write-Host ''
        Write-Host 'Raven is still running:' -ForegroundColor Yellow
        foreach ($p in $running) { Write-Host "  $($p.ProcessName) (process $($p.Id))" }
        Write-Host 'Best: right-click the Raven icon in the notification area (near the clock) > Quit and stop the'
        Write-Host 'background program, then run uninstall.ps1 again.'
        $answer = ''
        if (-not $NoPause) { $answer = Read-Host 'Or type stop and press Enter to end them now (anything else cancels)' }
        if ($answer.Trim() -ne 'stop') { Fail 'Nothing was removed.' }
        foreach ($p in $running) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        foreach ($p in $running) { try { Wait-Process -Id $p.Id -Timeout 10 -ErrorAction SilentlyContinue } catch { } }
        if (@(Get-RavenProcesses $dest).Count -gt 0) { Fail 'Raven is still running; nothing was removed.' }
    }

    # The Start menu entry, if it is the one install.ps1 made (it points into the app folder).
    if (Test-Path -LiteralPath $shortcut) {
        $target = ''
        try { $target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath } catch { }
        if ($target -and $target.StartsWith($dest.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $shortcut -Force
            Write-Host '  removed the Start menu entry'
        } else {
            Write-Host "  left $shortcut as it is: it does not point to $dest"
        }
    }

    # The app folder (and leftovers of an interrupted install).
    $removed = $false
    foreach ($p in @($dest, "$dest.new", "$dest.old")) {
        if (Test-Path -LiteralPath $p) {
            if (-not (Test-Path -LiteralPath (Join-Path $p 'raven_client.dll')) -and $p -eq $dest) {
                Fail "$dest does not look like Raven for Windows; nothing was removed there."
            }
            Remove-Item -LiteralPath $p -Recurse -Force
            $removed = $true
        }
    }
    if ($removed) {
        Write-Host "  removed $dest"
        Write-Host ''
        Write-Host 'Raven is uninstalled.' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host "Raven was not installed in $dest."
    }

    # The data, which stays. The profile folder as Raven's engine finds it: RAVEN_DATA_DIR, else
    # ASH_DATA_DIR, else %USERPROFILE%\.raven-ash when only that one exists, else %USERPROFILE%\.raven.
    $data = $env:RAVEN_DATA_DIR
    if (-not $data) { $data = $env:ASH_DATA_DIR }
    if (-not $data) {
        $data = Join-Path $env:USERPROFILE '.raven'
        $older = Join-Path $env:USERPROFILE '.raven-ash'
        if (-not (Test-Path -LiteralPath $data) -and (Test-Path -LiteralPath $older -PathType Container)) { $data = $older }
    }
    $kept = @()
    $hasProfile = Test-Path -LiteralPath $data -PathType Container
    if ($hasProfile) {
        $kept += , @($data, 'your identity (its key protected by Windows for your user account), friends and messages')
    }
    $prefs = Join-Path $env:LOCALAPPDATA 'Raven'
    if (Test-Path -LiteralPath $prefs -PathType Container) {
        $kept += , @($prefs, 'this app''s own settings (theme, technical details)')
    }
    Write-Host ''
    if ($kept.Count -eq 0) {
        Write-Host 'No Raven data was found on this computer.'
        Finish 0
    }
    Write-Host 'Your Raven data was NOT deleted:'
    foreach ($k in $kept) {
        Write-Host "  $($k[0])"
        Write-Host "      $($k[1])"
    }
    Write-Host 'Installing Raven again brings it back.'
    Write-Host ''
    if (-not $hasProfile) {
        Write-Host 'To remove it too, delete the folder above yourself.'
        Finish 0
    }
    Write-Host 'To erase your Raven identity, friends and messages from this computer (this cannot be undone;'
    Write-Host 'friends then cannot reach you here until you set Raven up again), use one of these:'
    Write-Host ' - before uninstalling: Raven > Settings > Privacy > Erase Raven from this computer, when your'
    Write-Host '   version offers it (otherwise it shows "Preview");'
    Write-Host ' - Raven''s command-line tool, if you use it: raven erase'
    if ($kept.Count -eq 1) {
        Write-Host ' - or delete the folder above yourself, with Raven not running.'
    } else {
        Write-Host ' - or delete the folders above yourself, with Raven not running.'
    }
    Finish 0
} catch {
    Fail ("Raven was not removed completely: " + $_.Exception.Message)
}
