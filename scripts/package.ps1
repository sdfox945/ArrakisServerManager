param([string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
[xml]$properties=Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props')
$version=[string]$properties.Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'A stable application version is required.'}
$name='ArrakisServerManager-v'+$version+'-win-x64'
$release=Join-Path $projectRoot ('artifacts/release/'+$name)
$output=if($OutputDirectory){[IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))}else{Join-Path $projectRoot ('artifacts/release/packages-v'+$version)}
$stage=Join-Path $output $name
$zip=Join-Path $output ($name+'.zip')
if(!(Test-Path -LiteralPath (Join-Path $release 'ArrakisServerManager.exe'))){throw 'Publish the self-contained release first.'}
if((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)){throw 'Package output exists. Preserve it and choose a fresh version/output before rerunning.'}
New-Item -ItemType Directory -Path $stage -Force|Out-Null
foreach($file in Get-ChildItem -LiteralPath $release -Recurse -File){
 $relative=[IO.Path]::GetRelativePath($release,$file.FullName)
 if($file.Extension -eq '.pdb'){continue}
 if($relative -match '(?i)\.(ini|key|pem|pfx|p12|log|trace|db|backup|bak|partial|pak|uasset|ucas|utoc)$' -or $relative -match '(?i)(preferences|history|safety-state|application-update-cache)\.json$'){throw ('Runtime/private file refused: '+$relative)}
 $target=Join-Path $stage $relative;New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $target
}
$manifest=Join-Path $stage 'PACKAGE-SHA256SUMS.txt'
Get-ChildItem -LiteralPath $stage -Recurse -File|Sort-Object FullName|ForEach-Object{'{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant(),[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/')}|Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
$hash=(Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
('{0}  {1}' -f $hash,([IO.Path]::GetFileName($zip)))|Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Host ('Local package only: '+$zip)
Write-Host ('SHA-256: '+$hash)
