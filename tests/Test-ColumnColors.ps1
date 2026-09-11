param([string]$ApplicationPath, [string]$OutputDirectory, [switch]$ExpectRegression)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$ApplicationPath) { $ApplicationPath = Join-Path $project 'artifacts\To-do.exe' }
$ApplicationPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $project ('artifacts\column-colors-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$applicationCopy = Join-Path $OutputDirectory 'To-do.exe'
if ($ApplicationPath -eq $applicationCopy) { throw 'Use a dedicated test output directory, separate from the application.' }
Copy-Item -LiteralPath $ApplicationPath -Destination $applicationCopy
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$runner = Join-Path $OutputDirectory 'Test-ColumnColors.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ ('/win32manifest:' + (Join-Path $project 'src\app.manifest')) ('/out:' + $runner) ('/reference:' + $applicationCopy) @references (Join-Path $PSScriptRoot 'Test-ColumnColors.cs')
if ($LASTEXITCODE -ne 0) { throw 'Column color test compilation failed.' }
& $runner $OutputDirectory | Tee-Object -FilePath (Join-Path $OutputDirectory 'results.txt')
$testExitCode = $LASTEXITCODE
if ($ExpectRegression) {
    if ($testExitCode -ne 1) { throw "Expected a rendered-color regression; test exited $testExitCode." }
    'Expected color regression reproduced in the baseline build.'
} elseif ($testExitCode -ne 0) { throw "Column color regression test failed (exit $testExitCode)." }
