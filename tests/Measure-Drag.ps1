param([string]$ApplicationPath, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
$project = Split-Path $PSScriptRoot -Parent
if (!$ApplicationPath) { $ApplicationPath = Join-Path $project 'artifacts\To-do.exe' }
$ApplicationPath = (Resolve-Path -LiteralPath $ApplicationPath).Path
$benchDirectory = Join-Path $project 'artifacts\drag-benchmark'
New-Item -ItemType Directory -Path $benchDirectory -Force | Out-Null
if (!$OutputPath) { $OutputPath = Join-Path $benchDirectory ('drag-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json') }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (!(Test-Path -LiteralPath (Split-Path $OutputPath -Parent))) { throw 'The report output directory must already exist.' }
$runner = Join-Path $benchDirectory 'Measure-Drag.exe'
$source = Join-Path $PSScriptRoot 'Measure-Drag.cs'
if (!(Test-Path -LiteralPath $runner) -or (Get-Item -LiteralPath $source).LastWriteTimeUtc -gt (Get-Item -LiteralPath $runner).LastWriteTimeUtc) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll ('/win32manifest:' + (Join-Path $project 'src\app.manifest')) ('/out:' + $runner) $source
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the native drag measurement helper.' }
}
$dragVault = Join-Path $env:TEMP ('shike-drag-' + [Guid]::NewGuid().ToString('N'))
$dragState = Join-Path $dragVault 'state'
New-Item -ItemType Directory -Path $dragState -Force | Out-Null
@{ Vault = $dragVault; DesignVersion = 2; Left = 100; Top = 100; Width = 901.5; Height = 342; Transparency = 45; Pinned = $true; Zoom = 1 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dragState 'settings.json')
$dragApp = $null; $target = $null
try {
    # An explicit temporary vault disables resident mode; Close exits this instance.
    $dragApp = Start-Process -FilePath $ApplicationPath -ArgumentList @('--vault', ('"' + $dragVault + '"'), '--state-dir', ('"' + $dragState + '"')) -PassThru
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        Start-Sleep -Milliseconds 100
        $target = Get-ShikeWindow -AppId $dragApp.Id
        if ($null -ne $target) { break }
        if ($dragApp.HasExited) { break }
    }
    $errorPath = Join-Path $dragState 'error.log'
    if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Raw) }
    if ($null -eq $target) { throw 'The isolated drag test window did not appear.' }
    $targetHandle = [IntPtr]$target.Current.NativeWindowHandle
    Start-Sleep -Milliseconds 1000
    & $runner $targetHandle.ToInt64().ToString() $OutputPath
    if ($LASTEXITCODE -ne 0) { throw 'The native drag benchmark did not complete.' }
    $result = Get-Content -LiteralPath $OutputPath -Raw | ConvertFrom-Json
    $result | Add-Member -NotePropertyName ApplicationPath -NotePropertyValue $ApplicationPath
    $result | Add-Member -NotePropertyName ApplicationSha256 -NotePropertyValue (Get-FileHash -LiteralPath $ApplicationPath -Algorithm SHA256).Hash
    $result | Add-Member -NotePropertyName IsolatedVault -NotePropertyValue $dragVault
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath
    if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Raw) }
    $result | Select-Object Metric,ApplicationPath,InputSamples,NativePositionUpdates,InputIntervals,NativePositionIntervals,PollIntervals,ApplicationCpuMs,MaximumMaterialOffsetPx,MaterialMisalignedPolls | ConvertTo-Json -Depth 4
} finally {
    if ($null -ne $dragApp -and !$dragApp.HasExited) {
        try {
            if ($null -eq $target) { $target = Get-ShikeWindow -AppId $dragApp.Id }
            if ($null -ne $target) { $target.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
        } finally {
            if (!$dragApp.WaitForExit(3000)) { Stop-Process -Id $dragApp.Id -ErrorAction SilentlyContinue }
        }
    }
}
