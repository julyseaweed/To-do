param([string]$Tone='light',[string]$OutputPath,[switch]$KeepApp,[string]$ApplicationPath,[string]$ContentPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ProofOrder {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int t,uint f);
 [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr h,int index,IntPtr value);
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint command);
}
'@
$project = Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$backdropPath=Join-Path $project 'artifacts\Backdrop-v2.exe'
$backdropSource=Join-Path $PSScriptRoot 'Backdrop.cs'
if(!(Test-Path $backdropPath) -or (Get-Item $backdropSource).LastWriteTime -gt (Get-Item $backdropPath).LastWriteTime){
 & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/win32manifest:'+(Join-Path $project 'src\app.manifest')) ('/out:'+$backdropPath) $backdropSource
 if($LASTEXITCODE-ne 0){throw 'Could not build the controlled background'}
}
$proofVault = Join-Path $env:TEMP ('shike-proof-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $proofVault 'state') -Force | Out-Null
if($ContentPath){Copy-Item -LiteralPath $ContentPath -Destination (Join-Path $proofVault '当前.md')}
@{DesignVersion=2;Left=16;Top=16;Width=860;Height=330;Transparency=45;Pinned=$true;Zoom=1} | ConvertTo-Json | Set-Content (Join-Path $proofVault 'state/settings.json')
$proofApp = Start-Process -FilePath $ApplicationPath -ArgumentList @('--vault',('"' + $proofVault + '"'),'--state-dir',('"' + (Join-Path $proofVault 'state') + '"')) -PassThru
for($attempt=0;$attempt-lt 50;$attempt++){Start-Sleep -Milliseconds 100;$target=Get-ShikeWindow -AppId $proofApp.Id;if($null-ne$target){break}}
$errorPath=Join-Path $proofVault 'state\error.log';if(Test-Path $errorPath){throw(Get-Content $errorPath -Raw)}
if($null-eq$target){throw 'The app window did not appear'}
$proofHandle=[IntPtr]$target.Current.NativeWindowHandle
$nativeRect=[ProofOrder+Rect]::new();[ProofOrder]::GetWindowRect($proofHandle,[ref]$nativeRect)|Out-Null
$rect=[pscustomobject]@{X=$nativeRect.Left;Y=$nativeRect.Top;Width=$nativeRect.Right-$nativeRect.Left;Height=$nativeRect.Bottom-$nativeRect.Top}
$back = Start-Process -FilePath (Join-Path $project 'artifacts\Backdrop-v2.exe') -ArgumentList @([int]$rect.X,[int]$rect.Y,[int]$rect.Width,[int]$rect.Height,$Tone) -PassThru
Start-Sleep -Milliseconds 500
$referenceHandle=[ShikeWindowDiscovery]::FindTitle($back.Id,'To-do test background')
$backElement=if($referenceHandle-ne[IntPtr]::Zero){[System.Windows.Automation.AutomationElement]::FromHandle($referenceHandle)}else{$null}
$materialHandle=[ProofOrder]::GetWindow($proofHandle,4)
try {
 $back.Refresh()
 if($null-eq$backElement){throw 'The controlled background did not start'}
 if($materialHandle-ne[IntPtr]::Zero){[ProofOrder]::SetWindowLongPtr($materialHandle,-8,$referenceHandle)|Out-Null}
 [ProofOrder]::SetWindowPos($referenceHandle,[IntPtr](-1),[int]$rect.X,[int]$rect.Y,[int]$rect.Width,[int]$rect.Height,0x50) | Out-Null
 [ProofOrder]::SetWindowPos($proofHandle,[IntPtr](-1),0,0,0,0,0x13) | Out-Null
 Start-Sleep -Milliseconds 1600
 $proofApp.Refresh()
 $currentBounds=[ProofOrder+Rect]::new();[ProofOrder]::GetWindowRect($proofHandle,[ref]$currentBounds)|Out-Null
 if ($currentBounds.Bottom-$currentBounds.Top -lt 200) { throw 'Preview is no longer expanded; capture skipped.' }
 & (Join-Path $PSScriptRoot 'Inspect-Window.ps1') -AppId $proofApp.Id -WindowHandle $proofHandle -OutputPath $OutputPath
 [pscustomobject]@{AppId=$proofApp.Id;Vault=$proofVault;Tone=$Tone} | ConvertTo-Json -Compress
 $errorPath = Join-Path $proofVault 'state\error.log'
 if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Raw) }
} finally {
 if($materialHandle-ne[IntPtr]::Zero){[ProofOrder]::SetWindowLongPtr($materialHandle,-8,[IntPtr]::Zero)|Out-Null}
 if ($null -ne $backElement) { $backElement.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
 if (!$KeepApp) { $target.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); $proofApp.WaitForExit(2000) | Out-Null }
}
