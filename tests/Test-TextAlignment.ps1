param([string]$ApplicationPath,[string]$OutputDirectory,[switch]$ExpectRegression,[switch]$Extended,[switch]$TransitionsOnly,[switch]$TypographyBoardOnly)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('shike-text-alignment-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force|Out-Null
$copy=Join-Path $OutputDirectory 'To-do.exe'
if($ApplicationPath-ne$copy){Copy-Item -LiteralPath $ApplicationPath -Destination $copy}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references=@('System.dll','System.Core.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework ('WPF\'+$_))}
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:'+(Join-Path $OutputDirectory 'TextAlignment.Tests.exe')) ('/reference:'+$copy) @references (Join-Path $PSScriptRoot 'Test-TextAlignment.cs') (Join-Path $PSScriptRoot 'TextAlignmentAudit.cs')
if($LASTEXITCODE-ne0){throw 'Text-alignment test compilation failed.'}
& (Join-Path $OutputDirectory 'TextAlignment.Tests.exe') $OutputDirectory $(if($TypographyBoardOnly){'typography-board'}elseif($TransitionsOnly){'transitions'}elseif($Extended){'extended'}else{'standard'})|Tee-Object -FilePath (Join-Path $OutputDirectory 'results.txt')
$result=$LASTEXITCODE
if($ExpectRegression){if($result-ne1){throw ('Expected text-alignment regression; exit='+$result)}}elseif($result-ne0){throw ('Text-alignment regression failed: '+$OutputDirectory+'; exit='+$result)}
