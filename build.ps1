param([string]$OutputDirectory = 'artifacts')
$ErrorActionPreference = 'Stop'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$projectPath = $PSScriptRoot
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectPath $OutputDirectory }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $outputPath 'To-do.ico'
$sourceIcon = [System.Drawing.Image]::FromFile((Join-Path $projectPath 'src\app-icon.png'))
$iconImages = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $width = [int][Math]::Round($size * $sourceIcon.Width / $sourceIcon.Height)
    $graphics.DrawImage($sourceIcon,[int](($size-$width)/2),0,$width,$size)
    $png = [System.IO.MemoryStream]::new()
    $bitmap.Save($png,[System.Drawing.Imaging.ImageFormat]::Png)
    $iconImages += @{Size=$size;Bytes=$png.ToArray()}
    $png.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$sourceIcon.Dispose()
$iconStream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconImages.Count)
    $offset = 6 + 16 * $iconImages.Count
    foreach ($entry in $iconImages) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $iconImages) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $iconStream.Dispose() }
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath ('WPF\' + $_)) }
$references += @('System.Runtime.dll','System.Runtime.WindowsRuntime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Numerics.dll','System.Numerics.Vectors.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkPath $_) }
$references += @('Windows.UI.winmd','Windows.Foundation.winmd','Windows.Graphics.winmd') | ForEach-Object { '/reference:' + (Join-Path $env:WINDIR ('System32\WinMetadata\' + $_)) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectPath 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/win32icon:' + $iconPath) ('/resource:' + $iconPath + ',Shike.Icon') ('/resource:' + (Join-Path $projectPath 'src\app-icon.png') + ',Shike.Artwork') ('/win32manifest:' + (Join-Path $projectPath 'src\app.manifest')) ('/out:' + (Join-Path $outputPath 'To-do.exe')) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
& (Join-Path $frameworkPath 'csc.exe') /nologo /target:exe /platform:x64 /codepage:65001 ('/out:' + (Join-Path $outputPath 'To-do.Tests.exe')) @references ('/reference:' + (Join-Path $outputPath 'To-do.exe')) (Join-Path $projectPath 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
Get-Item -LiteralPath (Join-Path $outputPath 'To-do.exe') | Select-Object FullName,Length
