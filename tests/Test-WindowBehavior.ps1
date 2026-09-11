param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
if (!$ApplicationPath) { $ApplicationPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\To-do.exe' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowBehaviorProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int t,uint f);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h,IntPtr rgn);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr r,int x,int y);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
}
'@
[WindowBehaviorProbe]::SetProcessDPIAware() | Out-Null
$vault=Join-Path $env:TEMP ('shike-behavior-'+[Guid]::NewGuid().ToString('N'))
$state=Join-Path $vault 'state';New-Item -ItemType Directory -Path $state -Force | Out-Null
@{DesignVersion=2;Left=50;Top=60;Width=520;Height=320;Transparency=86;Pinned=$true;Zoom=1} | ConvertTo-Json | Set-Content (Join-Path $state 'settings.json')
$app=Start-Process $ApplicationPath -ArgumentList @('--vault',$vault,'--state-dir',$state,'--autostart') -PassThru
$cursor=[WindowBehaviorProbe+Point]::new();[WindowBehaviorProbe]::GetCursorPos([ref]$cursor) | Out-Null
function Find-Main {
 Get-ShikeWindow -AppId $app.Id
}
function Rect { $r=[WindowBehaviorProbe+Rect]::new();[WindowBehaviorProbe]::GetWindowRect($mainHandle,[ref]$r)|Out-Null;$r }
function Invoke-Control($name) { $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Assert($ok,$label) { if(!$ok){throw $label};Write-Output ('PASS '+$label) }
function Expand-NearBubble($bubbleBounds,[double]$panelWidth,[double]$panelHeight,[string]$label) {
 $work=[System.Windows.Forms.Screen]::FromHandle($mainHandle).WorkingArea
 $rightHalf=($bubbleBounds.Left+$bubbleBounds.Right)/2-ge($work.Left+$work.Right)/2
 $left=if($rightHalf){$bubbleBounds.Right-$panelWidth}else{$bubbleBounds.Left}
 $left=[Math]::Max($work.Left,[Math]::Min($work.Right-$panelWidth,$left))
 $top=[Math]::Max($work.Top,[Math]::Min($work.Bottom-$panelHeight,$bubbleBounds.Top))
 Invoke-Control 'Open list';Start-Sleep -Milliseconds 180
 $expanded=Rect
 Assert ([Math]::Abs($expanded.Left-$left)-le3-and[Math]::Abs($expanded.Top-$top)-le3-and[Math]::Abs(($expanded.Right-$expanded.Left)-$panelWidth)-le3-and[Math]::Abs(($expanded.Bottom-$expanded.Top)-$panelHeight)-le3) $label
}
try {
 for($attempt=0;$attempt-lt 50;$attempt++) {
  Start-Sleep -Milliseconds 100;$app.Refresh()
  $errorPath=Join-Path $state 'error.log';if(Test-Path $errorPath){throw(Get-Content $errorPath -Raw)}
  $root=Find-Main;if($null-ne$root){break}
 }
 Assert ($null-ne$root) 'Application starts without an error dialog'
 $mainHandle=[IntPtr]$root.Current.NativeWindowHandle
 $scale=[WindowBehaviorProbe]::GetDpiForWindow($mainHandle)/96.0
 $original=Rect
 foreach($corner in @(@{Name='top-left';X=1;Y=1;Hit=13},@{Name='top-right';X=-1;Y=1;Hit=14},@{Name='bottom-left';X=1;Y=-1;Hit=16},@{Name='bottom-right';X=-1;Y=-1;Hit=17})) {
  $cornerInset=[int][Math]::Round(14*$scale)
  $x=if($corner.X-eq 1){$original.Left+$cornerInset}else{$original.Right-$cornerInset}
  $y=if($corner.Y-eq 1){$original.Top+$cornerInset}else{$original.Bottom-$cornerInset}
  $packed=($y-shl 16)-bor($x-band 0xffff)
  $hit=[WindowBehaviorProbe]::SendMessage($mainHandle,0x84,[IntPtr]::Zero,[IntPtr]$packed).ToInt32()
  Assert ($hit-eq$corner.Hit) ($corner.Name+' has a diagonal resize hit area (actual '+$hit+', expected '+$corner.Hit+', size '+($original.Right-$original.Left)+'x'+($original.Bottom-$original.Top)+')')
  [WindowBehaviorProbe]::SetCursorPos($x,$y)|Out-Null
  [WindowBehaviorProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  for($step=1;$step-le 5;$step++) { [WindowBehaviorProbe]::SetCursorPos(($x-$corner.X*$step*12),($y-$corner.Y*$step*10))|Out-Null;Start-Sleep -Milliseconds 16 }
  [WindowBehaviorProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  $after=Rect
  Assert (($after.Right-$after.Left)-gt($original.Right-$original.Left)+35 -and ($after.Bottom-$after.Top)-gt($original.Bottom-$original.Top)+25) ($corner.Name+' mouse drag changes width and height together')
  [WindowBehaviorProbe]::SetWindowPos($mainHandle,[IntPtr]::Zero,$original.Left,$original.Top,($original.Right-$original.Left),($original.Bottom-$original.Top),0x14)|Out-Null
 }
 [WindowBehaviorProbe]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
 Invoke-Control 'Collapse / Expand';Start-Sleep -Milliseconds 330
 $small=Rect;$area=[System.Windows.Forms.Screen]::FromHandle($mainHandle).WorkingArea
 Assert (($small.Right-$small.Left)-eq($small.Bottom-$small.Top) -and [Math]::Abs(($small.Right-$small.Left)-64*$scale)-le 2) 'Collapsed window is a compact square enclosing the circular icon'
 Assert ([Math]::Abs($small.Right-($area.Right-12*$scale))-lt 4) 'Collapse docks at the right edge of the current screen'
 $region=[WindowBehaviorProbe]::CreateRectRgn(0,0,0,0)
 [WindowBehaviorProbe]::GetWindowRgn($mainHandle,$region)|Out-Null
 Assert (![WindowBehaviorProbe]::PtInRegion($region,[int](4*$scale),[int](12*$scale)) -and ![WindowBehaviorProbe]::PtInRegion($region,[int](59*$scale),[int](54*$scale)) -and [WindowBehaviorProbe]::PtInRegion($region,[int](28*$scale),[int](36*$scale)) -and [WindowBehaviorProbe]::PtInRegion($region,[int](54*$scale),[int](10*$scale))) 'Window region contains only the circle and close button, with no rounded rectangular plate'
 [WindowBehaviorProbe]::DeleteObject($region)|Out-Null
 $saved=Get-Content (Join-Path $state 'settings.json') -Raw | ConvertFrom-Json
 Assert ([Math]::Abs($saved.Left*$scale-$original.Left)-lt 3 -and [Math]::Abs($saved.Top*$scale-$original.Top)-lt 3) 'Collapsed settings keep the expanded position'
 & (Join-Path $PSScriptRoot 'Inspect-Window.ps1') -AppId $app.Id -WindowHandle $mainHandle -OutputPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\bubble-preview.png')
 $panelWidth=$original.Right-$original.Left;$panelHeight=$original.Bottom-$original.Top
 Expand-NearBubble $small $panelWidth $panelHeight 'Clicking the bubble expands beside its current position, clamped within the work area'
 for($i=0;$i-lt 3;$i++){
  Invoke-Control 'Collapse / Expand';Start-Sleep -Milliseconds 25
  Invoke-Control 'Open list';Start-Sleep -Milliseconds 180
  # UIA invocation is queued while the dock animation runs. Check its settled
  # result instead of comparing with a stale pre-invocation animation frame.
  $expanded=Rect;Start-Sleep -Milliseconds 120;$settled=Rect
  Assert ([Math]::Abs(($expanded.Right-$expanded.Left)-$panelWidth)-le3-and[Math]::Abs(($expanded.Bottom-$expanded.Top)-$panelHeight)-le3-and$expanded.Left-ge$area.Left-and$expanded.Top-ge$area.Top-and$expanded.Right-le$area.Right-and$expanded.Bottom-le$area.Bottom-and$settled.Left-eq$expanded.Left-and$settled.Top-eq$expanded.Top) ('Rapid collapse and expansion '+($i+1)+' keeps the panel within the work area and stops its dock animation')
 }
 $errorPath=Join-Path $state 'error.log';if(Test-Path $errorPath){throw(Get-Content $errorPath -Raw)}
} finally {
 [WindowBehaviorProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 [WindowBehaviorProbe]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
 if(!$app.HasExited){
  try {
   $quitRequest=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$state+'"'),'--vault',('"'+$vault+'"'),'--quit') -WindowStyle Hidden -PassThru
   if(!$quitRequest.WaitForExit(2500)){Stop-Process -Id $quitRequest.Id;throw 'The isolated quit request did not finish.'}
   $app.WaitForExit(2500)|Out-Null
  } catch { Write-Warning ('Could not quit isolated geometry test gracefully: '+$_.Exception.Message) }
  if(!$app.HasExited){Stop-Process -Id $app.Id}
 }
}
