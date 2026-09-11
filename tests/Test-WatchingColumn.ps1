param([string]$ApplicationPath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
$output=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $env:TEMP ('shike-watching-column-'+[Guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory -Path $output -Force|Out-Null
Copy-Item -LiteralPath $ApplicationPath -Destination (Join-Path $output 'To-do.exe')
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_))}
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $output 'Watching.Tests.exe')) ('/reference:'+(Join-Path $output 'To-do.exe')) @references (Join-Path $PSScriptRoot 'Test-WatchingColumn.cs')
if($LASTEXITCODE-ne0){throw 'Watching column test compilation failed.'}
& (Join-Path $output 'Watching.Tests.exe') $output | Tee-Object -FilePath (Join-Path $output 'results.txt')
if($LASTEXITCODE-ne0){throw ('Watching column regression failed. Results: '+(Join-Path $output 'results.txt'))}
