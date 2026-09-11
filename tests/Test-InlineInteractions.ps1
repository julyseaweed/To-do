param([string]$ApplicationPath,[string]$OutputDirectory,[switch]$ExpectRegression)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('shike-inline-interactions-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$copy=Join-Path $OutputDirectory 'To-do.exe'
if($ApplicationPath-ne$copy){Copy-Item -LiteralPath $ApplicationPath -Destination $copy}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_))}
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $OutputDirectory 'InlineInteractions.Tests.exe')) ('/reference:'+$copy) @references (Join-Path $PSScriptRoot 'Test-InlineInteractions.cs')
if($LASTEXITCODE-ne0){throw 'Inline interaction test compilation failed.'}
& (Join-Path $OutputDirectory 'InlineInteractions.Tests.exe') $OutputDirectory|Tee-Object -FilePath (Join-Path $OutputDirectory 'results.txt')
$result=$LASTEXITCODE
if($ExpectRegression){if($result-ne1){throw ('Expected interaction regression; exit='+$result)}}elseif($result-ne0){throw ('Interaction regression failed: '+$OutputDirectory+'; exit='+$result)}
