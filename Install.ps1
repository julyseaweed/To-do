param(
    [switch]$OpenAtLogin,
    # Kept for existing update scripts; shortcut-only setup is now the default.
    [switch]$IconsOnly
)
$ErrorActionPreference = 'Stop'

$applicationPath = Join-Path $PSScriptRoot 'To-do.exe'
if (!(Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    $applicationPath = Join-Path $PSScriptRoot 'artifacts\To-do.exe'
}
if (!(Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw 'To-do.exe was not found. Extract the complete release, or run build.ps1 first.'
}
$applicationPath = (Resolve-Path -LiteralPath $applicationPath).ProviderPath
$applicationDirectory = [System.IO.Path]::GetDirectoryName($applicationPath)
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$desktopShortcut = Join-Path $desktopDirectory 'To-do.lnk'

# Refresh a shortcut whose previous name differs only in capitalization.
if (Test-Path -LiteralPath $desktopShortcut) {
    $existingShortcut = Get-Item -LiteralPath $desktopShortcut
    if ($existingShortcut.Name -cne 'To-do.lnk') {
        $temporaryName = 'To-do-rename-' + [Guid]::NewGuid().ToString('N') + '.lnk'
        Rename-Item -LiteralPath $existingShortcut.FullName -NewName $temporaryName
        Rename-Item -LiteralPath (Join-Path $desktopDirectory $temporaryName) -NewName 'To-do.lnk'
    }
}

$shellObject = New-Object -ComObject WScript.Shell
$shortcut = $shellObject.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $applicationPath
$shortcut.WorkingDirectory = $applicationDirectory
$shortcut.Description = 'To-do - Desktop list'
$sourceIcon = Join-Path $applicationDirectory 'To-do.ico'
if (Test-Path -LiteralPath $sourceIcon -PathType Leaf) {
    $iconVersion = (Get-FileHash -LiteralPath $sourceIcon -Algorithm SHA256).Hash.Substring(0,12).ToLowerInvariant()
    $displayIcon = Join-Path $applicationDirectory ('ToDo-' + $iconVersion + '.ico')
    if (!(Test-Path -LiteralPath $displayIcon)) { Copy-Item -LiteralPath $sourceIcon -Destination $displayIcon }
    $shortcut.IconLocation = $displayIcon
} else {
    $shortcut.IconLocation = $applicationPath + ',0'
}
$shortcut.Save()

$startupKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
$startupRegistry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($startupKey, $true)
try {
    $alreadyEnabled = $null -ne $startupRegistry -and $null -ne $startupRegistry.GetValue('To-do')
    # Keep an enabled installation pointing at its new location. A fresh
    # installation stays off unless OpenAtLogin was explicitly requested.
    if ($OpenAtLogin -or $alreadyEnabled) {
        if ($null -eq $startupRegistry) {
            $startupRegistry = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($startupKey)
        }
        $startupRegistry.SetValue('To-do', ('"' + $applicationPath + '" --autostart'), [Microsoft.Win32.RegistryValueKind]::String)
    }
} finally {
    if ($null -ne $startupRegistry) { $startupRegistry.Dispose() }
}

if (-not ('ToDoShellRefresh' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ToDoShellRefresh {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    public static extern void SHChangeNotify(uint change, uint flags, string item1, string item2);
}
'@
}
[ToDoShellRefresh]::SHChangeNotify(0x2000,0x1005,$desktopShortcut,$null)
[ToDoShellRefresh]::SHChangeNotify(0x1000,0x1005,$desktopDirectory,$null)
[ToDoShellRefresh]::SHChangeNotify(0x8000000,0x1000,$null,$null)
Write-Output 'Desktop shortcut created. Double-click To-do to open your list.'
if ($OpenAtLogin -or $alreadyEnabled) { Write-Output 'Open at login is enabled.' }
