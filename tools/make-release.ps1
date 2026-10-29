<#
.SYNOPSIS
Builds the downloadable Windows package of Raven: Raven-windows-x64-<version>.zip and its .sha256.

.DESCRIPTION
Run on Windows 10 or 11 (x64) with Rust (rustup, MSVC toolchain), the Visual Studio Build Tools
(C++ workload) and the .NET 8 SDK, in Windows PowerShell 5.1 or PowerShell 7, from a clone with the
core submodule (git submodule update --init):

    powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 [-OutDir <folder>] [-DynamicCrt] [-NoCPathTrim]

Steps:
  1. Raven's engine from core\node: raven_client.dll, raven-node.exe and raven-tor-helper.exe,
     with `cargo build --release --locked --target x86_64-pc-windows-msvc -p raven-client
     -p raven-node --features raven-node/tor`. No nearby radio (no `raven-node/nearby-radio`):
     Raven for Windows has no Bluetooth. The C runtime is linked statically (+crt-static), so
     nothing needs vcruntime140.dll (-DynamicCrt turns that off). Build-machine paths are
     rewritten: rustc --remap-path-prefix for the user folder, CARGO_HOME, RUSTUP_HOME, the
     repository and the target folder; the C compiler's /d1trimfile: for the same folders.
  2. The app, published self-contained and unpackaged for win-x64 (.NET runtime and Windows App
     SDK runtime next to Raven.exe, no MSIX: WindowsPackageType=None and WindowsAppSDKSelfContained
     as Raven.Windows.csproj sets them), with the engine copied next to Raven.exe by the project
     (-p:RavenEngineDir). No debug symbols (DebugType=None).
  3. Checks of the payload: every expected file is there, no MockRaven, no Bluetooth radio, no
     .pdb, the engine's binaries are x64 and need no Visual C++ runtime DLL, and no file names a
     build-machine path (the user folder or user name, CARGO_HOME, RUSTUP_HOME, the NuGet cache,
     TEMP, the repository): the build fails if one does.
  4. <OutDir>\Raven-windows-x64-<version>.zip with Raven\ (the app), install.ps1, uninstall.ps1 and
     INSTALL-Windows.md, and <OutDir>\Raven-windows-x64-<version>.zip.sha256 ("<hash>  <name>").

The version is <Version> in Directory.Build.props. The engine comes from the raven-core commit
the core submodule points to: to package another raven-core, move the submodule
(git -C core checkout <commit>; git add core) and run this again. Raven\BUILD-INFO.txt in the zip
names both commits.

Nothing here runs a Raven program: a release Raven runs on your real profile (%USERPROFILE%\.raven).
The app is assembled in a work folder under -OutDir that is removed at the end, so no release
Raven.exe is left lying around there. The package is not signed.

.PARAMETER OutDir
Where the zip and the .sha256 go (default: dist in the repository).

.PARAMETER DynamicCrt
Link the engine against the Visual C++ runtime DLLs (vcruntime140.dll) instead of statically. A
package built so needs the Visual C++ Redistributable (x64) on the computers it is installed on.

.PARAMETER NoCPathTrim
Don't pass /d1trimfile: (an MSVC switch that is not documented) to the C compiler, for a compiler
that refuses it. The payload check still stops the build if a C file's path is left in a binary.
#>
[CmdletBinding()]
param(
    [string]$OutDir = '',
    [switch]$DynamicCrt,
    [switch]$NoCPathTrim
)

$ErrorActionPreference = 'Stop'

$RustTarget = 'x86_64-pc-windows-msvc'
$NodeFeatures = 'raven-node/tor'
$Repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Core = Join-Path $Repo 'core'
$NodeDir = Join-Path $Core 'node'
$AppProject = Join-Path $Repo 'src\Raven.Windows\Raven.Windows.csproj'
$Latin1 = [System.Text.Encoding]::GetEncoding(28591)

function Say([string]$text) { Write-Host "==> $text" }

function Stop-Build([string]$text) {
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::error::$text" }
    throw $text
}

function Warn([string]$text) {
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::warning::$text" }
    Write-Warning $text
}

# Runs a program, stops when it fails. Its output goes to the console as it comes.
function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { Stop-Build "$exe $($arguments -join ' ') failed (exit code $LASTEXITCODE)" }
}

# One line of a program's standard output, or '' when it fails.
function Get-Line([string]$exe, [string[]]$arguments) {
    try {
        $out = & $exe @arguments 2>$null
        if ($LASTEXITCODE -ne 0 -or $null -eq $out) { return '' }
        return (@($out)[0]).ToString().Trim()
    } catch {
        return ''
    }
}

function Get-Commit([string]$dir) {
    $sha = Get-Line 'git' @('-C', $dir, 'rev-parse', 'HEAD')
    if ($sha -eq '') { return 'unknown (not a git checkout)' }
    try {
        $changed = & git -C $dir status --porcelain --untracked-files=no 2>$null
        if ($LASTEXITCODE -eq 0 -and "$changed".Trim() -ne '') { return "$sha (with local changes)" }
    } catch { }
    return $sha
}

function Get-FullPath([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return '' }
    return [System.IO.Path]::GetFullPath($path).TrimEnd('\', '/')
}

# --- The PE header of a program or library: its machine and the DLLs it imports. ---------------
function Read-UInt32([byte[]]$b, [int]$at) { return [BitConverter]::ToUInt32($b, $at) }

function Get-PeInfo([string]$path) {
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b.Length -lt 64 -or $b[0] -ne 0x4D -or $b[1] -ne 0x5A) { return $null }
    $pe = [BitConverter]::ToInt32($b, 0x3C)
    if ($pe -lt 0 -or $pe + 24 -gt $b.Length -or (Read-UInt32 $b $pe) -ne 0x00004550) { return $null }
    $machine = [BitConverter]::ToUInt16($b, $pe + 4)
    $sectionCount = [BitConverter]::ToUInt16($b, $pe + 6)
    $optionalSize = [BitConverter]::ToUInt16($b, $pe + 20)
    $optional = $pe + 24
    $magic = [BitConverter]::ToUInt16($b, $optional)
    if ($magic -eq 0x20B) { $dirs = $optional + 112 } elseif ($magic -eq 0x10B) { $dirs = $optional + 96 } else { return $null }
    $dirCount = Read-UInt32 $b ($dirs - 4)
    $sections = $optional + $optionalSize
    $toOffset = {
        param([uint32]$rva)
        for ($i = 0; $i -lt $sectionCount; $i++) {
            $s = $sections + 40 * $i
            $va = Read-UInt32 $b ($s + 12)
            $size = [Math]::Max((Read-UInt32 $b ($s + 8)), (Read-UInt32 $b ($s + 16)))
            if ($rva -ge $va -and $rva -lt $va + $size) { return [int64]($rva - $va + (Read-UInt32 $b ($s + 20))) }
        }
        return [int64]-1
    }
    $imports = New-Object System.Collections.Generic.List[string]
    # Directory 1: imports (20-byte descriptors, name at +12); 13: delay-load imports (32 bytes, name at +4).
    foreach ($d in @(@(1, 20, 12), @(13, 32, 4))) {
        if ($dirCount -le $d[0]) { continue }
        $rva = Read-UInt32 $b ($dirs + 8 * $d[0])
        if ($rva -eq 0) { continue }
        $at = & $toOffset $rva
        while ($at -ge 0 -and $at + $d[1] -le $b.Length) {
            $nameRva = Read-UInt32 $b ([int]($at + $d[2]))
            if ($nameRva -eq 0) { break }
            $nameAt = & $toOffset $nameRva
            if ($nameAt -lt 0) { break }
            $end = [Array]::IndexOf($b, [byte]0, [int]$nameAt)
            if ($end -lt 0) { break }
            $imports.Add([System.Text.Encoding]::ASCII.GetString($b, [int]$nameAt, [int]($end - $nameAt)).ToLowerInvariant())
            $at += $d[1]
        }
    }
    return New-Object PSObject -Property @{ Machine = $machine; Imports = $imports }
}

# True when the file holds $text in ASCII/UTF-8 or UTF-16LE (ordinal, case-insensitive).
function Test-FileHasText([string]$path, [string[]]$texts) {
    $content = $Latin1.GetString([System.IO.File]::ReadAllBytes($path)).ToLowerInvariant()
    foreach ($t in $texts) {
        $lower = $t.ToLowerInvariant()
        $wide = $Latin1.GetString([System.Text.Encoding]::Unicode.GetBytes($lower))
        if ($content.IndexOf($lower, [StringComparison]::Ordinal) -ge 0) { return $true }
        if ($content.IndexOf($wide, [StringComparison]::Ordinal) -ge 0) { return $true }
    }
    return $false
}

# --- Inputs ------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath (Join-Path $NodeDir 'Cargo.toml'))) {
    Stop-Build "core\node\Cargo.toml is missing: fetch the raven-core submodule first (git submodule update --init)."
}
[xml]$props = Get-Content -LiteralPath (Join-Path $Repo 'Directory.Build.props') -Raw
$Version = "$($props.Project.PropertyGroup.Version)".Trim()
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$') {
    Stop-Build "Directory.Build.props has no usable <Version> (found '$Version')."
}
[xml]$packages = Get-Content -LiteralPath (Join-Path $Repo 'Directory.Packages.props') -Raw
$AppSdk = ($packages.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'Microsoft.WindowsAppSDK' } | Select-Object -First 1).Version

if ($OutDir -eq '') { $OutDir = Join-Path $Repo 'dist' }
# Relative to the current PowerShell location (not the process's current directory).
$OutDir = Get-FullPath $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$Name = "Raven-windows-x64-$Version"
$Zip = Join-Path $OutDir "$Name.zip"
$Sha = "$Zip.sha256"

foreach ($tool in 'cargo', 'rustc', 'dotnet', 'git') {
    if ($null -eq (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Stop-Build "$tool is not on PATH (needed: Rust with the MSVC toolchain, the .NET 8 SDK, Git)."
    }
}
$Rustc = Get-Line 'rustc' @('--version')
$Dotnet = Get-Line 'dotnet' @('--version')
if ($Dotnet -notmatch '^8\.') { Warn "The .NET SDK in use is $Dotnet; the app targets .NET 8 (CI uses an 8.0.x SDK)." }
$AppCommit = Get-Commit $Repo
$CoreCommit = Get-Commit $Core
Say "Raven for Windows $Version (x64): app $AppCommit, core $CoreCommit"
Say "$Rustc; .NET SDK $Dotnet; Windows App SDK $AppSdk"

# The engine's build hashes the vendored SQLite byte for byte: a CRLF checkout (core.autocrlf)
# breaks it, so say so before a long build fails on it.
$eol = $null
try { $eol = & git -C $Core ls-files --eol -- node/third_party protocol shared-vectors 2>$null } catch { }
if ($LASTEXITCODE -eq 0 -and $null -ne $eol) {
    $crlf = @($eol | Where-Object { $_ -match '^i/lf\s+w/(\S*)' -and $Matches[1] -ne 'lf' -and $Matches[1] -ne 'none' })
    if ($crlf.Count -gt 0) {
        Write-Host ($crlf | Select-Object -First 10 | Out-String)
        Stop-Build "Files in core that must be LF are not LF in this checkout (core.autocrlf). Clone again with: git -c core.autocrlf=false clone --recurse-submodules https://github.com/Raven-ASHCO/raven-windows.git"
    }
}

# --- Build-machine paths: rewritten in the binaries, then looked for in the payload. ------------
$UserHome = Get-FullPath $env:USERPROFILE
$CargoHome = Get-FullPath $(if ($env:CARGO_HOME) { $env:CARGO_HOME } else { Join-Path $env:USERPROFILE '.cargo' })
$RustupHome = Get-FullPath $(if ($env:RUSTUP_HOME) { $env:RUSTUP_HOME } else { Join-Path $env:USERPROFILE '.rustup' })
$NugetRoot = Get-FullPath $(if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' })
$CargoTarget = Get-FullPath $(if ($env:CARGO_TARGET_DIR) { $env:CARGO_TARGET_DIR } else { Join-Path $NodeDir 'target' })
$RepoRoot = Get-FullPath $Repo
$EngineDir = Join-Path $CargoTarget "$RustTarget\release"

# Most general first: when several match, rustc uses the last one.
$remaps = New-Object System.Collections.Generic.List[object]
foreach ($pair in @(@($UserHome, '/home'), @($RepoRoot, '/raven-windows'), @($CargoTarget, '/target'),
        @($RustupHome, '/rustup'), @($CargoHome, '/cargo'))) {
    if ($pair[0] -ne '') { $remaps.Add($pair) }
}
$rustFlags = New-Object System.Collections.Generic.List[string]
foreach ($r in $remaps) { $rustFlags.Add("--remap-path-prefix=$($r[0])=$($r[1])") }
if (-not $DynamicCrt) { $rustFlags.Add('-Ctarget-feature=+crt-static') }
$cFlags = New-Object System.Collections.Generic.List[string]
foreach ($r in $remaps) {
    if ($NoCPathTrim) { break }
    if ($r[0] -match '\s') { Warn "'$($r[0])' has a space: the C compiler can't be told to trim it."; continue }
    $cFlags.Add("/d1trimfile:$($r[0])\")
}

$saved = @{}
foreach ($v in 'CARGO_ENCODED_RUSTFLAGS', 'RUSTFLAGS', 'CFLAGS', 'CARGO_INCREMENTAL') {
    $saved[$v] = [Environment]::GetEnvironmentVariable($v, 'Process')
}
$Work = Join-Path $OutDir ("work-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    # --- 1. The engine -----------------------------------------------------------------------
    if ($saved['RUSTFLAGS'] -or $saved['CARGO_ENCODED_RUSTFLAGS']) { Warn 'RUSTFLAGS is set: ignored for this build.' }
    Remove-Item Env:RUSTFLAGS -ErrorAction SilentlyContinue
    $env:CARGO_ENCODED_RUSTFLAGS = $rustFlags -join [char]0x1f
    $env:CFLAGS = $cFlags -join ' '
    $env:CARGO_INCREMENTAL = '0'
    Say "Building raven-client, raven-node and raven-tor-helper (release, $RustTarget, features: $NodeFeatures, C runtime $(if ($DynamicCrt) { 'dynamic' } else { 'static' }))"
    Push-Location $NodeDir
    try {
        Invoke-Checked 'cargo' @('build', '--release', '--locked', '--target', $RustTarget,
            '-p', 'raven-client', '-p', 'raven-node', '--features', $NodeFeatures)
    } finally {
        Pop-Location
    }
    $Engine = @('raven_client.dll', 'raven-node.exe', 'raven-tor-helper.exe')
    foreach ($f in $Engine) {
        if (-not (Test-Path -LiteralPath (Join-Path $EngineDir $f))) { Stop-Build "$f is missing in $EngineDir after the build." }
    }

    # --- 2. The app, self-contained and unpackaged ---------------------------------------------
    $App = Join-Path $Work 'Raven'
    Say "Publishing Raven.exe (Release, win-x64, self-contained, unpackaged)"
    Invoke-Checked 'dotnet' @('publish', $AppProject, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:Platform=x64', '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=true',
        "-p:RavenEngineDir=$EngineDir/", '-p:DebugType=None', '-p:DebugSymbols=false',
        "-p:PathMap=$RepoRoot\=/_/", '-o', $App)

    # --- 3. Checks ---------------------------------------------------------------------------
    Say 'Checking the payload'
    # Microsoft.ui.xaml.dll: the Windows App SDK runtime is next to Raven.exe (self-contained);
    # coreclr.dll and hostfxr.dll: so is the .NET runtime.
    $required = @('Raven.exe', 'Raven.dll', 'Raven.Windows.Core.dll', 'raven_client.dll', 'raven-node.exe',
        'raven-tor-helper.exe', 'Microsoft.ui.xaml.dll', 'coreclr.dll', 'hostfxr.dll', 'Assets\Raven.ico')
    foreach ($f in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $App $f))) { Stop-Build "$f is missing next to Raven.exe: the publish is not self-contained or lacks the engine." }
    }
    if (@(Get-ChildItem -LiteralPath $App -Filter '*.pri' -File).Count -eq 0) { Stop-Build 'No .pri resource file next to Raven.exe: WinUI could not load its XAML.' }
    if (Test-Path -LiteralPath (Join-Path $App 'raven-nearby-helper.exe')) { Stop-Build 'raven-nearby-helper.exe is in the payload: Raven for Windows has no Bluetooth.' }
    Get-ChildItem -LiteralPath $App -Recurse -Force -File | Where-Object { $_.Extension -eq '.pdb' } | Remove-Item -Force
    # MockRaven only exists in Debug builds (as CI checks).
    if (Test-FileHasText (Join-Path $App 'Raven.Windows.Core.dll') @('Raven.Windows.Core.Mock')) { Stop-Build 'MockRaven is in the Release core library.' }
    # No Bluetooth radio: the WinRT Bluetooth classes are named in raven-node only with nearby-radio.
    foreach ($f in $Engine) {
        if (Test-FileHasText (Join-Path $App $f) @('Windows.Devices.Bluetooth')) { Stop-Build "$f contains the Bluetooth radio (Windows.Devices.Bluetooth): it must be built without raven-node/nearby-radio." }
    }
    # The engine: x64, and (static C runtime) no Visual C++ runtime DLL.
    $crtDll = '^(vcruntime140(_1)?|msvcp140(_\w+)?|ucrtbase|concrt140|vccorlib140)\.dll$|^api-ms-win-crt-'
    foreach ($f in @($Engine + 'Raven.exe')) {
        $info = Get-PeInfo (Join-Path $App $f)
        if ($null -eq $info) { Stop-Build "$f is not a Windows program or library." }
        if ($info.Machine -ne 0x8664) { Stop-Build ("{0} is not x64 (machine 0x{1:X4})." -f $f, $info.Machine) }
        if (-not $DynamicCrt -and $f -ne 'Raven.exe') {
            $crt = @($info.Imports | Where-Object { $_ -match $crtDll })
            if ($crt.Count -gt 0) { Stop-Build "$f needs $($crt -join ', '): the C runtime is not linked statically." }
        }
    }
    # Anything in the payload that needs a Visual C++ runtime DLL the payload doesn't carry.
    $present = @{}
    Get-ChildItem -LiteralPath $App -Recurse -Force -File | ForEach-Object { $present[$_.Name.ToLowerInvariant()] = $true }
    $needsRedist = New-Object System.Collections.Generic.List[string]
    foreach ($file in @(Get-ChildItem -LiteralPath $App -Recurse -Force -File | Where-Object { $_.Extension -eq '.dll' -or $_.Extension -eq '.exe' })) {
        $info = Get-PeInfo $file.FullName
        if ($null -eq $info) { continue }
        foreach ($dll in $info.Imports) {
            if ($dll -match '^(vcruntime140(_1)?|msvcp140(_\w+)?|concrt140|vccorlib140)\.dll$' -and -not $present.ContainsKey($dll)) {
                $needsRedist.Add("$($file.Name) -> $dll")
            }
        }
    }
    if ($needsRedist.Count -gt 0) {
        Warn ("These files need the Visual C++ Redistributable (x64), which the package does not carry: " + (($needsRedist | Select-Object -Unique) -join '; '))
    }
    # No build-machine path anywhere (ASCII/UTF-8, UTF-16, with \, / or \\ separators).
    $roots = @($UserHome, $CargoHome, $RustupHome, $NugetRoot, $CargoTarget, $RepoRoot,
        (Get-FullPath $env:TEMP), (Get-FullPath $env:TMP)) | Where-Object { $_.Length -ge 6 } | Select-Object -Unique
    $needles = New-Object System.Collections.Generic.List[string]
    foreach ($r in $roots) {
        foreach ($sep in '\', '/', '\\') { $needles.Add(($r -replace '[\\/]', $sep) + $sep) }
    }
    if ($env:USERNAME -and $env:USERNAME.Length -ge 2) {
        foreach ($sep in '\', '/', '\\') { $needles.Add("${sep}Users$sep$($env:USERNAME)$sep") }
    }
    $leaks = New-Object System.Collections.Generic.List[string]
    foreach ($file in Get-ChildItem -LiteralPath $App -Recurse -Force -File) {
        if (Test-FileHasText $file.FullName $needles) { $leaks.Add($file.FullName.Substring($App.Length + 1)) }
    }
    if ($leaks.Count -gt 0) {
        Stop-Build ("These files name a folder of the build machine (user folder, cargo, NuGet, TEMP or the repository): " + ($leaks -join ', '))
    }
    Say "Payload ok: $(@(Get-ChildItem -LiteralPath $App -Recurse -File).Count) files, engine x64$(if (-not $DynamicCrt) { ' with a static C runtime' }), no build-machine paths"

    # --- 4. The zip --------------------------------------------------------------------------
    Copy-Item -LiteralPath (Join-Path $Repo 'LICENSE'), (Join-Path $Repo 'NOTICE') -Destination $App
    $buildInfo = @(
        "Raven for Windows $Version (x64)",
        '',
        "App:  https://github.com/Raven-ASHCO/raven-windows commit $AppCommit",
        "Core: https://github.com/Raven-ASHCO/raven-core commit $CoreCommit",
        "Engine: raven_client.dll, raven-node.exe and raven-tor-helper.exe (release; raven-node features: $NodeFeatures; no Bluetooth radio); C runtime $(if ($DynamicCrt) { 'dynamic (needs the Visual C++ Redistributable)' } else { 'static' })",
        "App: .NET 8 and Windows App SDK $AppSdk, self-contained, unpackaged; not signed",
        "Built with: $Rustc; .NET SDK $Dotnet",
        "Built: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm')) UTC",
        '',
        'Source code: the two commits above (GNU AGPL-3.0: see LICENSE and NOTICE).'
    )
    [System.IO.File]::WriteAllText((Join-Path $App 'BUILD-INFO.txt'), (($buildInfo -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding $false))
    foreach ($f in @('tools\package\install.ps1', 'tools\package\uninstall.ps1', 'INSTALL-Windows.md')) {
        Copy-Item -LiteralPath (Join-Path $Repo $f) -Destination $Work
    }

    Say "Writing $Zip"
    if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip -Force }
    if (Test-Path -LiteralPath $Sha) { Remove-Item -LiteralPath $Sha -Force }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $entries = Get-ChildItem -LiteralPath $Work -Recurse -Force -File |
        ForEach-Object { New-Object PSObject -Property @{ Path = $_.FullName; Name = ($_.FullName.Substring($Work.Length + 1) -replace '\\', '/') } } |
        Sort-Object -Property Name
    $archive = [System.IO.Compression.ZipFile]::Open($Zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($e in $entries) {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e.Path, $e.Name, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally {
        $archive.Dispose()
    }
    $hash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($Sha, "$hash  $Name.zip`n", (New-Object System.Text.UTF8Encoding $false))
    Say "Done: $Zip"
    Say "SHA-256: $hash"
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value @(
            "### $Name.zip", '',
            "- SHA-256: ``$hash``",
            "- App commit: ``$AppCommit``",
            "- Core commit: ``$CoreCommit``",
            "- Engine: raven-node features ``$NodeFeatures``, no Bluetooth radio, C runtime $(if ($DynamicCrt) { 'dynamic' } else { 'static' })",
            "- Not signed")
    }
} finally {
    if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($v in $saved.Keys) { [Environment]::SetEnvironmentVariable($v, $saved[$v], 'Process') }
}
