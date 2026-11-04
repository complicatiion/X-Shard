<#
.SYNOPSIS
 Builds the self-contained X-Shard release: portable ZIP and (optionally) the Inno Setup installer.
.DESCRIPTION
 Runs from PowerShell or automatically after a Visual Studio Release build (see XShardPackageRelease in XShard.csproj).
 Steps: backend/legal/license checks -> legal RTF pages -> GUI source accompaniment -> dotnet publish of X-Shard.exe
 (self-contained single file) and xshard-cli.exe (self-contained, trimmed single file) into one folder -> portable ZIP ->
 Inno Setup installer -> SHA-256 sidecars.
.PARAMETER BuildInstaller
 Also compile installer/setup.iss with Inno Setup 6.3 or newer.
.PARAMETER IsccPath
 Explicit path to ISCC.exe if it cannot be discovered (PATH, registry, Program Files, per-user install).
.PARAMETER RunVerification
 Run the service and headless UI verification harnesses before publishing.
.PARAMETER FromMSBuild
 Set by the Visual Studio/MSBuild trigger. Errors are written in MSBuild format so they appear in the Error List.
#>
[CmdletBinding()]
param([switch]$BuildInstaller, [string]$IsccPath, [switch]$RunVerification, [switch]$FromMSBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
# When started from a Visual Studio build, MSBuild variables of the hosting VS instance would leak into the nested
# dotnet CLI calls and can select a different MSBuild/SDK. Remove them so dotnet uses its own SDK resolution.
foreach ($name in @('MSBuildSDKsPath','MSBUILD_EXE_PATH','MSBuildExtensionsPath','MSBuildLoadMicrosoftTargetsReadOnly','MSBUILDNOINPROCNODE','VSINSTALLDIR')) {
 if (Test-Path -LiteralPath "Env:$name") { Remove-Item -LiteralPath "Env:$name" }
}
$watch = [Diagnostics.Stopwatch]::StartNew()
$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo 'artifacts'
$sourceStage = $null

function Step([string]$text) { Write-Host ("[X-Shard {0:mm\:ss}] {1}" -f $watch.Elapsed, $text) }
function Invoke-Checked([string]$what, [scriptblock]$command) { & $command; if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." } }
function Find-Iscc {
 if ($IsccPath) { return $IsccPath }
 $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
 if ($command) { return $command.Source }
 foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
                    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
  $location = $null
  if (Test-Path -LiteralPath $key) { $location = (Get-Item -LiteralPath $key).GetValue('InstallLocation') }
  if ($location) { $candidate = Join-Path $location 'ISCC.exe'; if (Test-Path -LiteralPath $candidate) { return $candidate } }
 }
 foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs' }))) {
  if ($base) { $candidate = Join-Path $base 'Inno Setup 6\ISCC.exe'; if (Test-Path -LiteralPath $candidate) { return $candidate } }
 }
 return $null
}
function Write-Hash([string]$file) {
 $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
 "$hash  $(Split-Path $file -Leaf)" | Set-Content -LiteralPath "$file.sha256" -Encoding ASCII
}

try {
 $project = Join-Path $repo 'src\XShard\XShard.csproj'
 [xml]$projectXml = Get-Content -LiteralPath $project
 $version = [string]($projectXml.Project.PropertyGroup | Where-Object { $_['Version'] } | Select-Object -First 1)['Version'].InnerText
 if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'No valid project version found in XShard.csproj.' }
 Step "Release $version"

 # Inno Setup is checked first so a missing compiler fails fast instead of after a full publish.
 if ($BuildInstaller) {
  $IsccPath = Find-Iscc
  if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Inno Setup 6.3+ not found. Install it, pass -IsccPath, or disable the installer step (-p:XShardBuildInstaller=false).' }
  $isccVersion = [version](Get-Item -LiteralPath $IsccPath).VersionInfo.FileVersion.Split(' ')[0]
  if ($isccVersion -lt [version]'6.3') { throw "Inno Setup $isccVersion found; 6.3 or newer is required (x64compatible architecture identifiers)." }
  Step "Inno Setup $isccVersion : $IsccPath"
 }

 Step 'Checking bundled backends'
 $backend = Join-Path $repo 'src\XShard\tools\extract-xiso.exe'
 if (!(Test-Path -LiteralPath $backend)) { throw 'Bundled extract-xiso.exe is missing (src\XShard\tools).' }
 Invoke-Checked 'extract-xiso self-test' { & $backend -v | Out-Null }
 $xgd = Join-Path $repo 'src\XShard\tools\XGDTool.exe'
 if (!(Test-Path -LiteralPath $xgd)) { throw 'Bundled XGDTool.exe is missing (src\XShard\tools).' }
 Invoke-Checked 'XGDTool self-test' { & $xgd --version | Out-Null }

 Step 'Checking legal documents and licenses'
 foreach ($name in @('THIRD_PARTY_LICENSES.md','LICENSE_DECLARATION.md','EULA.md','PRIVACY_POLICY.md','LEGAL_NOTICE.md','COPYRIGHT.md','GPL-3.0.md')) {
  $legal = Join-Path $repo "docs\legal\$name"
  if (!(Test-Path -LiteralPath $legal) -or [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $legal -Raw))) { throw "Publisher document is missing: docs\legal\$name" }
 }
 # Root LICENSE.md is authoritative; licenses\X-Shard-LICENSE.md is kept as an identical copy.
 $license = Join-Path $repo 'LICENSE.md'; $licenseCopy = Join-Path $repo 'licenses\X-Shard-LICENSE.md'
 if (!(Test-Path -LiteralPath $license)) { throw 'Root LICENSE.md is missing.' }
 if (!(Test-Path -LiteralPath $licenseCopy) -or (Get-FileHash -LiteralPath $license).Hash -ne (Get-FileHash -LiteralPath $licenseCopy).Hash) {
  Copy-Item -LiteralPath $license -Destination $licenseCopy -Force; Step 'licenses\X-Shard-LICENSE.md synchronized with root LICENSE.md'
 }
 if (!(Test-Path -LiteralPath (Join-Path $repo 'sources\XGDTool-source-v1.0.0.zip'))) { throw 'XGDTool source accompaniment is missing (sources\XGDTool-source-v1.0.0.zip).' }

 $publish = Join-Path $artifacts 'publish'
 $portable = Join-Path $artifacts 'portable'
 foreach ($path in @($publish, $portable)) {
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
  New-Item -ItemType Directory -Path $path -Force | Out-Null
 }

 if ($RunVerification) {
  Step 'Running service verification'
  Invoke-Checked 'Service verification' { & dotnet run --project (Join-Path $PSScriptRoot 'verification\XShard.Verification.csproj') -p:XShardReleaseScript=true -- $backend }
  Step 'Running headless UI verification'
  Invoke-Checked 'UI verification' { & dotnet run --project (Join-Path $PSScriptRoot 'verification-ui\XShard.UiCheck.csproj') -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PublishSingleFile=false -p:XShardReleaseScript=true -- $repo }
 }

 Step 'Rendering installer legal pages'
 Invoke-Checked 'Legal document rendering' { & dotnet run --project (Join-Path $PSScriptRoot 'LegalDocuments.Build\LegalDocuments.Build.csproj') -c Release -- $repo }

 # Regenerate the exact GUI source accompaniment (GPL) before each binary release.
 Step 'Creating GUI source accompaniment'
 $sourceStage = Join-Path $artifacts ('gui-source-' + [guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
 Get-ChildItem -LiteralPath $repo -File -Recurse -Force | ForEach-Object {
  $relative = $_.FullName.Substring($repo.Length + 1)
  if ($relative -notmatch '(^|[\\/])(bin|obj|artifacts|\.git|\.vs|\.avalonia-build-tasks|TestResults)([\\/]|$)' -and
      $relative -notmatch '^sources[\\/]X-Shard-source-' -and $relative -notmatch '\.(user|suo)$' -and $_.Extension -ne '.exe') {
   $target = Join-Path $sourceStage $relative
   New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
   Copy-Item -LiteralPath $_.FullName -Destination $target -Force
  }
 }
 Get-ChildItem -LiteralPath (Join-Path $repo 'sources') -Filter 'X-Shard-source-*.zip' | Where-Object Name -ne "X-Shard-source-$version.zip" | Remove-Item -Force
 $guiSource = Join-Path $repo "sources\X-Shard-source-$version.zip"
 Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $guiSource -Force

 # XShardReleaseScript=true prevents the post-build trigger from starting this script recursively.
 # No IntermediateOutputPath/OutDir overrides: as global properties they would also apply to XShard.Core and let projects share one obj folder.
 Step 'Publishing self-contained win-x64 single file'
 Invoke-Checked 'dotnet publish' { & dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish --nologo `
  -p:XShardReleaseScript=true }
 if (!(Test-Path (Join-Path $publish 'X-Shard.exe'))) { throw 'Published executable is missing.' }
 Step 'Publishing xshard-cli.exe (trimmed single file, same folder)'
 $cli = Join-Path $repo 'src\XShard.Cli\XShard.Cli.csproj'
 Invoke-Checked 'dotnet publish (CLI)' { & dotnet publish $cli -c Release -r win-x64 --self-contained true -o $publish --nologo `
  -p:XShardReleaseScript=true }
 Invoke-Checked 'xshard-cli smoke test' { & (Join-Path $publish 'xshard-cli.exe') version | Out-Null }
 foreach ($name in @('backends\extract-xiso\extract-xiso.exe','backends\XGDTool\XGDTool.exe','sources\XGDTool-source-v1.0.0.zip',"sources\X-Shard-source-$version.zip",
                     'xshard-cli.exe','licenses\X-Shard-LICENSE.md','licenses\XGDTool-GPL-3.0.txt','licenses\GPL-3.0.txt','LICENSE.md','legal\EULA.md','Localization\en.xml','Localization\pl.xml','Localization\da.xml','Assets\toast-icon.png')) {
  if (!(Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Required published component is missing: $name" }
 }

 Step 'Creating portable ZIP'
 Copy-Item -Path (Join-Path $publish '*') -Destination $portable -Recurse -Force
 New-Item -ItemType File -Path (Join-Path $portable 'portable.flag') -Force | Out-Null
 $zip = Join-Path $artifacts "X-Shard_v${version}_win-x64_portable.zip"
 Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
 Write-Hash $zip

 if ($BuildInstaller) {
  Step 'Compiling Inno Setup installer'
  Invoke-Checked 'Inno Setup compilation' { & $IsccPath /Q "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'setup.iss') }
  $setup = Join-Path $artifacts "X-Shard_v${version}_win-x64_setup.exe"
  if (!(Test-Path -LiteralPath $setup)) { throw 'Installer was not created.' }
  Write-Hash $setup
 }
 Step "Done. Release files: $artifacts"
}
catch {
 if ($FromMSBuild) { Write-Host "publish-portable.ps1 : error XSB001: $($_.Exception.Message)" } else { Write-Error $_ -ErrorAction Continue }
 exit 1
}
finally {
 if ($sourceStage -and (Test-Path -LiteralPath $sourceStage)) { Remove-Item -LiteralPath $sourceStage -Recurse -Force -ErrorAction SilentlyContinue }
}
exit 0
