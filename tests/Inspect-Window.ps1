param([int]$AppId, [string]$OutputPath, [IntPtr]$WindowHandle=[IntPtr]::Zero, [switch]$RequireActive)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShikeWindowCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
 public static bool Activate(IntPtr hwnd) {
  if(GetForegroundWindow()==hwnd)return true;
  uint ignored;uint current=GetCurrentThreadId();uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);
  bool attached=foreground!=current&&AttachThreadInput(current,foreground,true);
  try {SetForegroundWindow(hwnd);return GetForegroundWindow()==hwnd;}
  finally {if(attached)AttachThreadInput(current,foreground,false);}
 }
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
 [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
 [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint operation);
}
'@
[ShikeWindowCapture]::SetProcessDPIAware() | Out-Null
$app = Get-Process -Id $AppId
$handle = $WindowHandle
if($handle-eq[IntPtr]::Zero){
 . (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
 $mainWindow=Get-ShikeWindow -AppId $AppId
 $handle=if($null-ne$mainWindow){[IntPtr]$mainWindow.Current.NativeWindowHandle}else{$app.MainWindowHandle}
}
if ($handle -eq [IntPtr]::Zero) {
 $windowCondition = [System.Windows.Automation.AndCondition]::new(
  [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$AppId),
  [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window))
 $appWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$windowCondition)
 if ($null -eq $appWindow) { throw 'The requested app panel is hidden; capture skipped.' }
 $handle = [IntPtr]$appWindow.Current.NativeWindowHandle
}
$wasTopmost = ([ShikeWindowCapture]::GetWindowLong($handle,-20) -band 8) -ne 0
try {
[ShikeWindowCapture]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
[ShikeWindowCapture]::Activate($handle) | Out-Null
Start-Sleep -Milliseconds 600
if($RequireActive -and [ShikeWindowCapture]::GetForegroundWindow()-ne$handle){throw 'The requested window could not be activated; material capture skipped.'}
$nativeRect=[ShikeWindowCapture+Rect]::new()
if(![ShikeWindowCapture]::GetWindowRect($handle,[ref]$nativeRect)){throw 'Cannot read native window bounds'}
$rect=[pscustomobject]@{X=$nativeRect.Left;Y=$nativeRect.Top;Width=$nativeRect.Right-$nativeRect.Left;Height=$nativeRect.Bottom-$nativeRect.Top}
foreach ($fraction in @(0.3,0.5,0.7)) {
 $probe = [ShikeWindowCapture+Point]::new(); $probe.X = [int]($rect.X+$rect.Width*$fraction); $probe.Y = [int]($rect.Y+$rect.Height*0.56)
 $cover = [ShikeWindowCapture]::GetAncestor([ShikeWindowCapture]::WindowFromPoint($probe),2)
 if ($cover -ne $handle) { $coverPid=[uint32]0;[ShikeWindowCapture]::GetWindowThreadProcessId($cover,[ref]$coverPid)|Out-Null;$coverName=(Get-Process -Id $coverPid -ErrorAction SilentlyContinue).ProcessName;throw "The requested app is occluded at $($probe.X),$($probe.Y) by window $cover in $coverName (expected $handle); no screenshot was taken." }
}
$bitmap = [System.Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height,[System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
 $screenDc=[ShikeWindowCapture]::GetDC([IntPtr]::Zero);$bitmapDc=$graphics.GetHdc()
 try { if(![ShikeWindowCapture]::BitBlt($bitmapDc,0,0,$bitmap.Width,$bitmap.Height,$screenDc,[int]$rect.X,[int]$rect.Y,0x40CC0020)){throw 'Desktop capture failed'} }
 finally {$graphics.ReleaseHdc($bitmapDc);[ShikeWindowCapture]::ReleaseDC([IntPtr]::Zero,$screenDc)|Out-Null}
 $bitmap.Save($OutputPath,[System.Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose() }
} finally {
 $restoreOrder = if ($wasTopmost) { [IntPtr](-1) } else { [IntPtr](-2) }
 [ShikeWindowCapture]::SetWindowPos($handle, $restoreOrder, 0, 0, 0, 0, 0x13) | Out-Null
}
Write-Output $OutputPath
