<#
.SYNOPSIS
 Rebuilds the bundled extract-xiso.exe from the frozen source in .\sources, tests it and (optionally) replaces the bundled copy.
.DESCRIPTION
 Developer tool. Not part of the normal release build: the release uses src\XShard\tools\extract-xiso.exe as shipped.

 Steps:
  1. Finds a compiler: MinGW-w64 GCC (preferred, matches docs\nfo\BACKEND_PROVENANCE.md) or MSVC (cl.exe via vswhere/vcvars64).
  2. Builds into artifacts\backend-build (never directly over the bundled file).
  3. Tests the new binary: version output plus a create/list/extract round-trip with byte comparison.
  4. Checks the PE imports: no VCRUNTIME/MSVCP/libgcc/libwinpthread/libstdc++ dependencies allowed.
  5. Prints SHA-256 and imports, then replaces src\XShard\tools\extract-xiso.exe with a timestamped backup
     (skip with -BuildOnly) and optionally updates BACKEND_PROVENANCE.md (-UpdateProvenance).

 The upstream source includes win32\dirent.c, win32\getopt.c and (MSVC only) win32\asprintf.c itself via #include,
 so only extract-xiso.c is passed to the compiler.
.PARAMETER Compiler
 Auto (default: MinGW first, then MSVC), MinGW or MSVC.
.PARAMETER GccPath
 Explicit path to a MinGW-w64 x86_64 gcc.exe.
.PARAMETER BuildOnly
 Build and test only; do not replace the bundled extract-xiso.exe.
.PARAMETER UpdateProvenance
 After replacing, update compiler, build command, SHA-256 and imports in docs\nfo\BACKEND_PROVENANCE.md.
.EXAMPLE
 .\installer\rebuild-extract-xiso.ps1 -BuildOnly
.EXAMPLE
 .\installer\rebuild-extract-xiso.ps1 -Compiler MinGW -GccPath C:\msys64\mingw64\bin\gcc.exe -UpdateProvenance
#>
[CmdletBinding()]
param(
 [ValidateSet('Auto','MinGW','MSVC')][string]$Compiler = 'Auto',
 [string]$GccPath,
 [switch]$BuildOnly,
 [switch]$UpdateProvenance
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo       = Split-Path $PSScriptRoot -Parent
$sourceDir  = Join-Path $repo 'sources'
$source     = Join-Path $sourceDir 'extract-xiso.c'
$bundled    = Join-Path $repo 'src\XShard\tools\extract-xiso.exe'
$provenance = Join-Path $repo 'docs\nfo\BACKEND_PROVENANCE.md'
$work       = Join-Path $repo 'artifacts\backend-build'
$output     = Join-Path $work 'extract-xiso.exe'

# Native tools write diagnostics to stderr; in Windows PowerShell 5.1 that becomes terminating errors under "Stop".
# All native calls go through this helper (stderr merged as text, exit code in $LASTEXITCODE).
function Invoke-Native([string]$exe, [string[]]$arguments) {
 $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
 try { return @(& $exe @arguments 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $previous }
}
function Step([string]$text) { Write-Host "[extract-xiso] $text" -ForegroundColor Cyan }
function Fail([string]$text) { throw "rebuild-extract-xiso: $text" }

# ---------- source ----------
if (!(Test-Path -LiteralPath $source)) { Fail "Source not found: $source" }
foreach ($name in 'dirent.c','getopt.c','asprintf.c') {
 if (!(Test-Path -LiteralPath (Join-Path $sourceDir "win32\$name"))) { Fail "Missing Windows support file: sources\win32\$name" }
}
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Path $work -Force | Out-Null

# ---------- compiler discovery ----------
function Test-MinGW([string]$gcc) {
 if (!$gcc -or !(Test-Path -LiteralPath $gcc)) { return $false }
 $machine = (Invoke-Native $gcc @('-dumpmachine')) -join ''
 return $LASTEXITCODE -eq 0 -and $machine -match 'x86_64' -and $machine -match 'mingw'
}
function Find-MinGW {
 if ($GccPath) { if (Test-MinGW $GccPath) { return $GccPath }; Fail "-GccPath is not an x86_64 MinGW-w64 gcc: $GccPath" }
 $candidates = New-Object System.Collections.Generic.List[string]
 foreach ($name in 'x86_64-w64-mingw32-gcc.exe','gcc.exe') {
  $command = Get-Command $name -ErrorAction SilentlyContinue
  if ($command) { $candidates.Add($command.Source) }
 }
 foreach ($folder in 'C:\msys64\mingw64\bin','C:\msys64\ucrt64\bin','C:\mingw64\bin','C:\winlibs\mingw64\bin','C:\ProgramData\chocolatey\lib\mingw\tools\install\mingw64\bin') {
  $candidates.Add((Join-Path $folder 'gcc.exe'))
 }
 foreach ($candidate in $candidates) { if (Test-MinGW $candidate) { return $candidate } }
 return $null
}
function Import-MsvcEnvironment {
 if (Get-Command cl.exe -ErrorAction SilentlyContinue) { return $true }
 $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
 if (!(Test-Path -LiteralPath $vswhere)) { return $false }
 $installation = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
 if (!$installation) { return $false }
 $vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvars64.bat'
 if (!(Test-Path -LiteralPath $vcvars)) { return $false }
 # Load the x64 developer environment into this PowerShell process.
 $lines = & cmd.exe /d /c "`"$vcvars`" >nul 2>&1 && set"
 foreach ($line in $lines) { if ($line -match '^([^=]+)=(.*)$') { Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2] } }
 return [bool](Get-Command cl.exe -ErrorAction SilentlyContinue)
}

$gcc = $null; $useMsvc = $false
if ($Compiler -in 'Auto','MinGW') { $gcc = Find-MinGW }
if (!$gcc) {
 if ($Compiler -eq 'MinGW') { Fail 'MinGW-w64 x86_64 gcc not found. Install MSYS2 (pacman -S mingw-w64-x86_64-gcc) or pass -GccPath.' }
 if (Import-MsvcEnvironment) { $useMsvc = $true }
 else { Fail 'No compiler found. Install MinGW-w64 (preferred) or Visual Studio with "Desktop development with C++".' }
}

# ---------- build ----------
# Built inside artifacts\backend-build (objects land there); the #include "win32/..." lines resolve relative to the source file.
Push-Location $work
try {
 if ($useMsvc) {
  $compilerText = ((Invoke-Native 'cl.exe' @() | Select-Object -First 1) -join '').Trim()
  Step "Compiler: MSVC ($compilerText)"
  $buildCommand = 'cl /nologo /O2 /MT /D_CRT_SECURE_NO_WARNINGS /D_CRT_NONSTDC_NO_WARNINGS extract-xiso.c'
  Invoke-Native 'cl.exe' @('/nologo','/O2','/MT','/D_CRT_SECURE_NO_WARNINGS','/D_CRT_NONSTDC_NO_WARNINGS',$source,"/Fe$output") | Write-Host
 }
 else {
  $compilerText = "MinGW GCC $((Invoke-Native $gcc @('-dumpversion')) -join '') ($((Invoke-Native $gcc @('-dumpmachine')) -join ''))"
  Step "Compiler: $compilerText -> $gcc"
  if ($gcc -match '\\ucrt64\\') { Write-Warning 'UCRT64 toolchain: the binary will import the Universal C Runtime (part of Windows 10/11) instead of msvcrt.dll as documented.' }
  $buildCommand = 'x86_64-w64-mingw32-gcc -O2 -static -o extract-xiso.exe extract-xiso.c'
  Invoke-Native $gcc @('-O2','-static','-o',$output,$source) | Write-Host
 }
 if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $output)) { Fail "Compilation failed (exit $LASTEXITCODE)." }
}
finally { Pop-Location }
Step "Built: $output"

# ---------- tests ----------
Step 'Test: version'
$version = (Invoke-Native $output @('-v')) -join ' '
if ($LASTEXITCODE -ne 0 -or $version -notmatch 'extract-xiso') { Fail "Version test failed: $version" }
Write-Host "  $version"

Step 'Test: create / list / extract round-trip'
$test = Join-Path $work 'roundtrip'
$fixture = Join-Path $test 'input'; $extracted = Join-Path $test 'extracted'; $image = Join-Path $test 'test.iso'
New-Item -ItemType Directory -Path (Join-Path $fixture 'nested') -Force | Out-Null
New-Item -ItemType Directory -Path $extracted -Force | Out-Null
$payload = New-Object byte[] 70000
for ($i = 0; $i -lt $payload.Length; $i++) { $payload[$i] = [byte]($i % 251) }
[IO.File]::WriteAllBytes((Join-Path $fixture 'nested\sample.bin'), $payload)
[IO.File]::WriteAllText((Join-Path $fixture 'readme.txt'), 'X-Shard backend round-trip fixture')
Invoke-Native $output @('-m','-c',$fixture,$image) | Out-Null
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $image)) { Fail 'Create test failed.' }
$listing = (Invoke-Native $output @('-l',$image)) -join "`n"
if ($LASTEXITCODE -ne 0 -or $listing -notmatch 'sample\.bin') { Fail "List test failed:`n$listing" }
Invoke-Native $output @('-d',$extracted,'-x',$image) | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'Extract test failed.' }
$roundtrip = Get-ChildItem -LiteralPath $extracted -Recurse -Filter 'sample.bin' | Select-Object -First 1
if (!$roundtrip) { Fail 'Extracted file not found.' }
$expected = (Get-FileHash -LiteralPath (Join-Path $fixture 'nested\sample.bin') -Algorithm SHA256).Hash
if ((Get-FileHash -LiteralPath $roundtrip.FullName -Algorithm SHA256).Hash -ne $expected) { Fail 'Round-trip content differs.' }
Write-Host '  Round-trip identical.'

# ---------- PE import check ----------
function Get-PeImports([string]$path) {
 $bytes = [IO.File]::ReadAllBytes($path)
 $pe = [BitConverter]::ToInt32($bytes, 0x3C)
 if ([Text.Encoding]::ASCII.GetString($bytes, $pe, 4) -ne "PE`0`0") { Fail 'Not a PE file.' }
 $sections = [BitConverter]::ToUInt16($bytes, $pe + 6)
 $optionalSize = [BitConverter]::ToUInt16($bytes, $pe + 20)
 $optional = $pe + 24
 if ([BitConverter]::ToUInt16($bytes, $optional) -ne 0x20B) { Fail 'Not a 64-bit (PE32+) executable.' }
 $importRva = [BitConverter]::ToUInt32($bytes, $optional + 112 + 8)   # data directory 1 (import table)
 $table = $optional + $optionalSize
 function ToOffset([uint32]$rva) {
  for ($s = 0; $s -lt $sections; $s++) {
   $h = $table + $s * 40
   $va = [BitConverter]::ToUInt32($bytes, $h + 12); $size = [Math]::Max([BitConverter]::ToUInt32($bytes, $h + 8), [BitConverter]::ToUInt32($bytes, $h + 16))
   if ($rva -ge $va -and $rva -lt $va + $size) { return [int]($rva - $va + [BitConverter]::ToUInt32($bytes, $h + 20)) }
  }
  return -1
 }
 $names = New-Object System.Collections.Generic.List[string]
 $descriptor = ToOffset $importRva
 while ($descriptor -ge 0) {
  $nameRva = [BitConverter]::ToUInt32($bytes, $descriptor + 12)
  if ($nameRva -eq 0) { break }
  $nameOffset = ToOffset $nameRva; $end = [Array]::IndexOf($bytes, [byte]0, $nameOffset)
  $names.Add([Text.Encoding]::ASCII.GetString($bytes, $nameOffset, $end - $nameOffset))
  $descriptor += 20
 }
 return $names
}
$imports = @(Get-PeImports $output)
Step "Imports: $($imports -join ', ')"
$forbidden = @($imports | Where-Object { $_ -match '^(vcruntime|msvcp|libgcc|libwinpthread|libstdc\+\+)' })
if ($forbidden.Count -gt 0) { Fail "Redistributable runtime dependency found: $($forbidden -join ', '). Link statically (-static or /MT)." }

# ---------- result ----------
$newHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
$oldHash = if (Test-Path -LiteralPath $bundled) { (Get-FileHash -LiteralPath $bundled -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' }
Step "SHA-256 new:     $newHash"
if ($oldHash) { Step "SHA-256 bundled: $oldHash" }

if ($BuildOnly) { Step "Build only: bundled backend unchanged. Result: $output"; exit 0 }
if ($newHash -eq $oldHash) { Step 'Identical to the bundled backend; nothing to replace.'; exit 0 }

if (Test-Path -LiteralPath $bundled) {
 $backupDir = Join-Path $repo 'artifacts\backend-backup'
 New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
 $backup = Join-Path $backupDir ("extract-xiso-{0:yyyyMMdd-HHmmss}-{1}.exe" -f (Get-Date), $oldHash.Substring(0, 8))
 Copy-Item -LiteralPath $bundled -Destination $backup
 Step "Backup: $backup"
}
Copy-Item -LiteralPath $output -Destination $bundled -Force
Step "Replaced: $bundled"

if ($UpdateProvenance -and (Test-Path -LiteralPath $provenance)) {
 $text = [IO.File]::ReadAllText($provenance)
 $text = [regex]::Replace($text, '(?m)^- Windows x64 binary rebuilt from the supplied C sources using .*$', "- Windows x64 binary rebuilt from the supplied C sources using $compilerText (rebuild-extract-xiso.ps1, $(Get-Date -Format 'yyyy-MM-dd')).")
 $text = [regex]::Replace($text, '(?m)^- Build: `.*`\.$', "- Build: ``$buildCommand``.")
 # The extract-xiso section lists the bundled hash; replace exactly that value (the XGDTool hash stays untouched).
 if ($oldHash) { $text = $text.Replace($oldHash, $newHash) }
 $text = [regex]::Replace($text, '(?m)^- PE imports: .*$', "- PE imports: $($imports -join ', '). No redistributable runtime dependency.")
 [IO.File]::WriteAllText($provenance, $text, (New-Object Text.UTF8Encoding($false)))
 Step "Updated: $provenance"
}
elseif (!$UpdateProvenance) {
 Write-Host ''
 Write-Host "Update docs\nfo\BACKEND_PROVENANCE.md (or rerun with -UpdateProvenance):" -ForegroundColor Yellow
 Write-Host "  Compiler: $compilerText"
 Write-Host "  Build:    $buildCommand"
 Write-Host "  SHA-256:  $newHash"
 Write-Host "  Imports:  $($imports -join ', ')"
}
Step 'Done. Run installer\publish-portable.ps1 to package the new backend.'
