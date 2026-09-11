param()
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$output = Join-Path $project 'artifacts\inline-data-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$inlineExe = Join-Path $output 'InlineNotes.Tests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll ('/out:' + $inlineExe) (Join-Path $project 'src\Notes.cs') (Join-Path $PSScriptRoot 'InlineNotesTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Inline data test compilation failed.' }
& $inlineExe
if ($LASTEXITCODE -ne 0) { throw 'Inline data tests failed.' }
$regressionExe = Join-Path $output 'Notes.Regression.Tests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.dll /reference:System.Core.dll ('/out:' + $regressionExe) (Join-Path $project 'src\Notes.cs') (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Existing note regression test compilation failed.' }
& $regressionExe
if ($LASTEXITCODE -ne 0) { throw 'Existing note regression tests failed.' }
