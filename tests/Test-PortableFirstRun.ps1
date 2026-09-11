param([string]$ApplicationPath)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$output = Join-Path $env:TEMP ('todo-portable-first-run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$inputs = @((Join-Path $PSScriptRoot 'Test-PortableFirstRun.cs'))
if ($ApplicationPath) {
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $ApplicationPath).Path -Destination (Join-Path $output 'To-do.exe')
    $inputs += '/reference:' + (Join-Path $output 'To-do.exe')
} else {
    $inputs += Join-Path $project 'src\Settings.cs'
    $inputs += Join-Path $project 'src\Notes.cs'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll ('/out:' + (Join-Path $output 'Portable.Tests.exe')) @inputs
if ($LASTEXITCODE -ne 0) { throw 'Portable first-run test compilation failed.' }
& (Join-Path $output 'Portable.Tests.exe') $output | Tee-Object -FilePath (Join-Path $output 'results.txt')
if ($LASTEXITCODE -ne 0) { throw ('Portable first-run regression failed. Results: ' + (Join-Path $output 'results.txt')) }
