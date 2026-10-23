param([switch]$Publish,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$localDotnet=Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet=if(Test-Path -LiteralPath $localDotnet){$localDotnet}else{'dotnet.exe'}
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools\cli'
$env:NUGET_PACKAGES=Join-Path $projectRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& $dotnet build 'ArrakisServerManager.sln' -c Release --disable-build-servers
if($LASTEXITCODE -ne 0){throw 'Release build failed.'}
if(-not $SkipTests){
 & $dotnet run --project 'tests\Arrakis.Tests\Arrakis.Tests.csproj' --no-build -c Release
 if($LASTEXITCODE -ne 0){throw 'Regression tests failed.'}
}
if($Publish){
 $release=Join-Path $projectRoot 'artifacts\release\ArrakisServerManager-v1.1.1-win-x64'
 & $dotnet publish 'src\Arrakis.Desktop\Arrakis.Desktop.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $release --disable-build-servers
 if($LASTEXITCODE -ne 0){throw 'Self-contained publish failed.'}
 Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $release
 Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $release
 Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $release -Recurse -Force
 @('@echo off','start "Arrakis Server Manager" "%~dp0ArrakisServerManager.exe" --demo --data-dir "%LOCALAPPDATA%\ArrakisServerManager\Demo-v1.1.1"')|Set-Content -LiteralPath (Join-Path $release 'Launch Demo.cmd') -Encoding Ascii
 @'
@echo off
setlocal
set "ARRAKIS_LAUNCH_DIR=%~dp0"
powershell.exe -NoProfile -Command "Start-Process -FilePath (Join-Path $env:ARRAKIS_LAUNCH_DIR 'ArrakisServerManager.exe') -Verb RunAs"
'@ | Set-Content -LiteralPath (Join-Path $release 'Launch as Administrator.cmd') -Encoding Ascii
 Get-ChildItem -LiteralPath $release -Recurse -File | ForEach-Object { $hash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; '{0}  {1}' -f $hash.Hash.ToLowerInvariant(),$_.FullName.Substring($release.Length+1) } | Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\release\SHA256SUMS-v1.1.1.txt') -Encoding utf8
 Write-Host "Runnable build: $release\ArrakisServerManager.exe"
}
