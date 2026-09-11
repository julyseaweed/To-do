param([string]$OutputDirectory = 'release')
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N')
$buildDirectory = 'artifacts\package-build-' + $runId
$stage = Join-Path $project ('artifacts\package-stage-' + $runId)
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $project $OutputDirectory }
New-Item -ItemType Directory -Path $stage,$destination -Force | Out-Null

# Build from source, never from the running app or its UserData directory.
& (Join-Path $project 'build.ps1') -OutputDirectory $buildDirectory
& (Join-Path $project ($buildDirectory + '\To-do.Tests.exe'))
if ($LASTEXITCODE -ne 0) { throw 'The package build did not pass its storage checks.' }
$application = Join-Path $project ($buildDirectory + '\To-do.exe')
& (Join-Path $project 'tests\Test-PortableFirstRun.ps1') -ApplicationPath $application
& (Join-Path $project 'tests\Test-SettingsPersistence.ps1') -ApplicationPath $application

$packageFiles = @('To-do.exe', 'To-do.ico', 'Install.ps1', 'README.md')
foreach ($name in $packageFiles) {
    $source = if ($name -in @('To-do.exe', 'To-do.ico')) { Join-Path $project ($buildDirectory + '\' + $name) } else { Join-Path $project $name }
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name)
}
$actual = @(Get-ChildItem -LiteralPath $stage -Force -File | Select-Object -ExpandProperty Name)
if (Compare-Object ($packageFiles | Sort-Object) ($actual | Sort-Object)) { throw 'Unexpected package contents.' }
if (@(Get-ChildItem -LiteralPath $stage -Force -Directory).Count) { throw 'A release must not include user-data folders.' }

$archive = Join-Path $destination 'To-do-windows-x64.zip'
Compress-Archive -LiteralPath @($packageFiles | ForEach-Object { Join-Path $stage $_ }) -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'), ($hash + '  To-do-windows-x64.zip' + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
Write-Output ('Release package: ' + $archive)
