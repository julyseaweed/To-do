param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
$output=Join-Path $env:TEMP ('shike-delete-empty-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force|Out-Null
Copy-Item -LiteralPath $ApplicationPath -Destination (Join-Path $output 'To-do.exe')
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_))}
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $output 'DeleteEmpty.Tests.exe')) ('/reference:'+(Join-Path $output 'To-do.exe')) @references (Join-Path $PSScriptRoot 'Test-DeleteEmpty.cs')
if($LASTEXITCODE-ne0){throw 'Empty-card deletion test compilation failed.'}
& (Join-Path $output 'DeleteEmpty.Tests.exe') $output
if($LASTEXITCODE-ne0){throw ('Empty-card deletion regression failed. Diagnostics: '+(Join-Path $output 'test-diagnostic.txt'))}
