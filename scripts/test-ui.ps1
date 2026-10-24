$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$output=Join-Path $projectRoot ('artifacts\ui-'+(Get-Date -Format yyyyMMdd-HHmmss))
$exe=Join-Path $projectRoot 'artifacts\release\ArrakisServerManager-v1.1.2-win-x64\ArrakisServerManager.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Publish the self-contained release first with scripts\build.ps1 -Publish.'}
$process=Start-Process -FilePath $exe -ArgumentList @('--ui-smoke',('"'+$output+'"'),'--data-dir',('"'+(Join-Path $output 'data')+'"')) -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw "UI smoke failed. Inspect $output\data\unhandled-error.txt"}
$result=Get-Content -Raw -LiteralPath (Join-Path $output 'result.json')|ConvertFrom-Json
if(-not $result.passed){throw 'UI smoke did not pass.'}
$bindingLog=Join-Path $output 'data\binding-errors.log'
if((Test-Path -LiteralPath $bindingLog) -and (Select-String -LiteralPath $bindingLog -Pattern 'Error:' -Quiet)){throw "WPF binding errors were recorded: $bindingLog"}
Write-Host "Demo UI rendered all six pages: $output"
