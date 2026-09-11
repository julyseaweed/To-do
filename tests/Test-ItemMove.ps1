param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$output=Join-Path $env:TEMP ('shike-item-move-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force|Out-Null
$inputs=@((Join-Path $PSScriptRoot 'Test-ItemMove.cs'))
if($ApplicationPath){
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $ApplicationPath).Path -Destination (Join-Path $output 'To-do.exe')
    $inputs+=('/reference:'+(Join-Path $output 'To-do.exe'))
}else{$inputs+=(Join-Path $project 'src\Notes.cs')}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll ('/out:'+(Join-Path $output 'ItemMove.Tests.exe')) @inputs
if($LASTEXITCODE-ne0){throw 'Item move test compilation failed.'}
& (Join-Path $output 'ItemMove.Tests.exe') $output | Tee-Object -FilePath (Join-Path $output 'results.txt')
if($LASTEXITCODE-ne0){throw ('Item move regression failed. Results: '+(Join-Path $output 'results.txt'))}
