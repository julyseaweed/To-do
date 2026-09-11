param([string]$ApplicationPath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('shike-item-reorder-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$copy=Join-Path $OutputDirectory 'To-do.exe'
if($ApplicationPath-ne$copy){Copy-Item -LiteralPath $ApplicationPath -Destination $copy}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_))}
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $OutputDirectory 'ItemReordering.Tests.exe')) ('/reference:'+$copy) @references (Join-Path $PSScriptRoot 'Test-ItemReordering.cs')
if($LASTEXITCODE-ne0){throw 'Item reorder test compilation failed.'}
& (Join-Path $OutputDirectory 'ItemReordering.Tests.exe') $OutputDirectory|Tee-Object -FilePath (Join-Path $OutputDirectory 'results.txt')
if($LASTEXITCODE-ne0){throw ('Item reorder regression failed: '+$OutputDirectory)}
