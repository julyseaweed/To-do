param([string]$ApplicationPath, [string]$OutputDirectory, [switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$ApplicationPath) { $ApplicationPath = Join-Path $project 'artifacts\To-do.exe' }
$ApplicationPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $project ('artifacts\horizontal-scroll-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$applicationCopy = Join-Path $OutputDirectory 'To-do.exe'
if ($ApplicationPath -eq $applicationCopy) { throw 'Use an isolated test output directory.' }
Copy-Item -LiteralPath $ApplicationPath -Destination $applicationCopy
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$runner = Join-Path $OutputDirectory 'Test-HorizontalScrolling.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ ('/win32manifest:' + (Join-Path $project 'src\app.manifest')) ('/out:' + $runner) ('/reference:' + $applicationCopy) @references (Join-Path $PSScriptRoot 'Test-HorizontalScrolling.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native horizontal scrolling test compilation failed.' }
if ($CompileOnly) { Write-Output $runner; return }
& $runner | Tee-Object -FilePath (Join-Path $OutputDirectory 'results.txt')
if ($LASTEXITCODE -ne 0) { throw "Native horizontal scrolling test failed (exit $LASTEXITCODE)." }
