param([string]$ApplicationPath,[string]$OutputDirectory,[switch]$MenuOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('shike-completion-style-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
Copy-Item -LiteralPath $ApplicationPath -Destination (Join-Path $OutputDirectory 'To-do.exe')
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_))}
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $OutputDirectory 'CompletionStyle.Tests.exe')) ('/reference:'+(Join-Path $OutputDirectory 'To-do.exe')) @references (Join-Path $PSScriptRoot 'Test-CompletionStyle.cs')
if($LASTEXITCODE-ne0){throw 'Completion-style test compilation failed.'}
& (Join-Path $OutputDirectory 'CompletionStyle.Tests.exe') $OutputDirectory $(if($MenuOnly){'menu'}else{'all'})|Tee-Object -FilePath (Join-Path $OutputDirectory 'results.txt')
if($LASTEXITCODE-ne0){throw ('Completion-style regression failed: '+$OutputDirectory)}
