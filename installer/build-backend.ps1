[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Run from an x64 Native Tools Command Prompt / Developer PowerShell for Visual Studio.
if (!(Get-Command cl.exe -ErrorAction SilentlyContinue)) { throw 'Run in an x64 Visual Studio C++ developer environment.' }
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'sources'
$target = Join-Path $repo 'src\XShard\tools\extract-xiso.exe'
$temp = Join-Path $repo 'artifacts\backend-build'
New-Item -ItemType Directory -Force -Path $temp | Out-Null
Push-Location $temp
try {
 & cl.exe /nologo /O2 /MT /D_CRT_SECURE_NO_WARNINGS (Join-Path $source 'extract-xiso.c') "/Fe:$target"
 if ($LASTEXITCODE -ne 0) { throw 'Backend compilation failed.' }
 & $target -v
 if ($LASTEXITCODE -ne 0) { throw 'Backend smoke test failed.' }
} finally { Pop-Location }
Write-Host 'Backend rebuilt with static C runtime. Update docs/nfo/BACKEND_PROVENANCE.md with the new hash.'
