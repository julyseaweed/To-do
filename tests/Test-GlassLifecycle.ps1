param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class GlassLifecycleProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
 [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
 [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public MouseInput Mouse; }
 delegate bool EnumProc(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int count);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int count);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] inputs,int size);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern int GetWindowRgn(IntPtr h,IntPtr region);
 [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")] public static extern bool PtInRegion(IntPtr region,int x,int y);
 [DllImport("gdi32.dll")] public static extern bool EqualRgn(IntPtr left,IntPtr right);
 [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
 public static IntPtr[] Windows(int processId) {
  var result=new List<IntPtr>();
  EnumWindows(delegate(IntPtr h,IntPtr unused){uint pid;GetWindowThreadProcessId(h,out pid);if(pid==processId)result.Add(h);return true;},IntPtr.Zero);
  return result.ToArray();
 }
 public static string Title(IntPtr h){var b=new StringBuilder(512);GetWindowText(h,b,b.Capacity);return b.ToString();}
 public static string Class(IntPtr h){var b=new StringBuilder(256);GetClassName(h,b,b.Capacity);return b.ToString();}
 public static bool Activate(IntPtr h) {
  if(GetForegroundWindow()==h)return true;
  uint ignored;uint current=GetCurrentThreadId(),foreground=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);
  bool attached=foreground!=0&&foreground!=current&&AttachThreadInput(current,foreground,true);
  try {SetForegroundWindow(h);return GetForegroundWindow()==h;}finally{if(attached)AttachThreadInput(current,foreground,false);}
 }
 public static void Click() {
  var down=new Input {Mouse=new MouseInput {Flags=2}};var up=new Input {Mouse=new MouseInput {Flags=4}};
  if(SendInput(2,new[]{down,up},Marshal.SizeOf(typeof(Input)))!=2)throw new InvalidOperationException("Activation click could not be delivered.");
 }
 public static void ReleaseMouse(){var up=new Input {Mouse=new MouseInput {Flags=4}};SendInput(1,new[]{up},Marshal.SizeOf(typeof(Input)));}
 public static string ForegroundDescription(){var h=GetForegroundWindow();uint pid;GetWindowThreadProcessId(h,out pid);return "HWND "+h+", PID "+pid+", class "+Class(h)+", title "+Title(h);}
}
'@
[GlassLifecycleProbe]::SetProcessDPIAware()|Out-Null
$vault=Join-Path $env:TEMP ('shike-glass-lifecycle-'+[Guid]::NewGuid().ToString('N'))
$state=Join-Path $vault 'state'
New-Item -ItemType Directory -Path $state -Force|Out-Null
@{Vault=$vault;DesignVersion=2;Left=16;Top=520;Width=520;Height=300;Transparency=45;Pinned=$false;Zoom=1}|ConvertTo-Json|Set-Content (Join-Path $state 'settings.json')
$app=$null;$other=$null;$mainHandle=[IntPtr]::Zero;$helperHandle=[IntPtr]::Zero;$otherHandle=[IntPtr]::Zero
$originalForeground=[GlassLifecycleProbe]::GetForegroundWindow();$originalCursor=[GlassLifecycleProbe+Point]::new(0,0)
[GlassLifecycleProbe]::GetCursorPos([ref]$originalCursor)|Out-Null
$checkCount=0
function Assert($ok,$label){if(!$ok){throw $label};$script:checkCount++;Write-Output ('PASS '+$label)}
function Wait-For([scriptblock]$predicate,[string]$label,[int]$timeout=2500){
 $watch=[System.Diagnostics.Stopwatch]::StartNew()
 do {
  if($null-ne$app -and $app.HasExited){throw ('App exited while waiting: '+$label)}
  $errorPath=Join-Path $state 'error.log';if(Test-Path $errorPath){throw(Get-Content $errorPath -Raw)}
  if(&$predicate){return}
  Start-Sleep -Milliseconds 80
 }while($watch.ElapsedMilliseconds-lt$timeout)
 throw ('Timed out: '+$label)
}
function Find-Main {
 Get-ShikeWindow -AppId $app.Id
}
function Find-Helper {
 foreach($handle in [GlassLifecycleProbe]::Windows($app.Id)) {
  if([GlassLifecycleProbe]::Title($handle)-eq'To-do glass' -and [GlassLifecycleProbe]::Class($handle)-eq'Message'){return $handle}
 }
 return [IntPtr]::Zero
}
function Bounds([IntPtr]$handle){$rect=[GlassLifecycleProbe+Rect]::new();[GlassLifecycleProbe]::GetWindowRect($handle,[ref]$rect)|Out-Null;return $rect}
function Same-Bounds {
 $main=Bounds $mainHandle;$helper=Bounds $helperHandle
 return [Math]::Abs($main.Left-$helper.Left)-le 1 -and [Math]::Abs($main.Top-$helper.Top)-le 1 -and [Math]::Abs($main.Right-$helper.Right)-le 1 -and [Math]::Abs($main.Bottom-$helper.Bottom)-le 1
}
function Assert-NormalLayer {
 Assert (([GlassLifecycleProbe]::GetWindowLongPtr($mainHandle,-20).ToInt64()-band 8)-eq 0) 'Main window stays outside the always-on-top band'
 Assert (([GlassLifecycleProbe]::GetWindowLongPtr($helperHandle,-20).ToInt64()-band 8)-eq 0) 'Glass helper stays outside the always-on-top band'
}
function Assert-BubbleLayer {
 Assert (([GlassLifecycleProbe]::GetWindowLongPtr($mainHandle,-20).ToInt64()-band 8)-ne 0) 'Collapsed bubble stays in the always-on-top band'
 Assert (([GlassLifecycleProbe]::GetWindowLongPtr($helperHandle,-20).ToInt64()-band 8)-ne 0) 'Collapsed glass follows the bubble into the always-on-top band'
}
function Assert-ExpandedRegion([string]$stage) {
 $rect=Bounds $mainHandle;$width=$rect.Right-$rect.Left;$height=$rect.Bottom-$rect.Top
 $scale=[GlassLifecycleProbe]::GetDpiForWindow($mainHandle)/96.0
 $mainRegion=[GlassLifecycleProbe]::CreateRectRgn(0,0,0,0)
 $helperRegion=[GlassLifecycleProbe]::CreateRectRgn(0,0,0,0)
 try {
  $mainKind=[GlassLifecycleProbe]::GetWindowRgn($mainHandle,$mainRegion)
  $helperKind=[GlassLifecycleProbe]::GetWindowRgn($helperHandle,$helperRegion)
  Assert ($mainKind-eq 3 -and $helperKind-eq 3 -and [GlassLifecycleProbe]::EqualRgn($mainRegion,$helperRegion)) ($stage+': foreground and glass have identical rounded clip regions')
  $cornersRounded=$true
  foreach($point in @(@{X=1;Y=1},@{X=$width-2;Y=1},@{X=1;Y=$height-2},@{X=$width-2;Y=$height-2})) {
   if([GlassLifecycleProbe]::PtInRegion($mainRegion,$point.X,$point.Y) -or [GlassLifecycleProbe]::PtInRegion($helperRegion,$point.X,$point.Y)){$cornersRounded=$false}
  }
  Assert $cornersRounded ($stage+': each outer corner is softly clipped instead of square')
  # These near-corner points fit the new smaller arc but were cut out by
  # the former large 22-DIP radius. Check actual native geometry, not its formula.
  $x=[int][Math]::Round(6*$scale);$y=[int][Math]::Round(2*$scale);$smallArcs=$true
  foreach($point in @(@{X=$x;Y=$y},@{X=$width-1-$x;Y=$y},@{X=$x;Y=$height-1-$y},@{X=$width-1-$x;Y=$height-1-$y})) {
   if(![GlassLifecycleProbe]::PtInRegion($mainRegion,$point.X,$point.Y) -or ![GlassLifecycleProbe]::PtInRegion($helperRegion,$point.X,$point.Y)){$smallArcs=$false}
  }
  Assert $smallArcs ($stage+': the restrained arcs retain content close to all four corners')
  $edgesComplete=$true
  foreach($point in @(@{X=1;Y=[int]($height/2)},@{X=$width-2;Y=[int]($height/2)},@{X=[int]($width/2);Y=1},@{X=[int]($width/2);Y=$height-2})) {
   if(![GlassLifecycleProbe]::PtInRegion($mainRegion,$point.X,$point.Y) -or ![GlassLifecycleProbe]::PtInRegion($helperRegion,$point.X,$point.Y)){$edgesComplete=$false}
  }
  Assert $edgesComplete ($stage+': rounded clipping still fills every straight window edge')
 }finally{
  [GlassLifecycleProbe]::DeleteObject($mainRegion)|Out-Null
  [GlassLifecycleProbe]::DeleteObject($helperRegion)|Out-Null
 }
}
function Invoke-Control([string]$name){
 $root=Find-Main
 if($null-eq$root){throw ('Main UI unavailable: '+$name)}
 $control=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))
 if($null-eq$control){throw ('Control unavailable: '+$name)}
 $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Activate-TestWindow([IntPtr]$handle){
 [GlassLifecycleProbe]::SetWindowPos($handle,[IntPtr]::Zero,0,0,0,0,0x53)|Out-Null
 [GlassLifecycleProbe]::Activate($handle)|Out-Null
 Start-Sleep -Milliseconds 100
 if([GlassLifecycleProbe]::GetForegroundWindow()-ne$handle){
  # The controlled form has no interactive content. Click only a verified
  # exposed point; the bubble's circle must never be clicked accidentally.
  $rect=Bounds $handle
  $points=@([GlassLifecycleProbe+Point]::new($rect.Left+10,$rect.Top+10),[GlassLifecycleProbe+Point]::new([int](($rect.Left+$rect.Right)/2),$rect.Top+20),[GlassLifecycleProbe+Point]::new($rect.Left+10,[int](($rect.Top+$rect.Bottom)/2)))
  foreach($point in $points){
   if([GlassLifecycleProbe]::GetAncestor([GlassLifecycleProbe]::WindowFromPoint($point),2)-ne$handle){continue}
   [GlassLifecycleProbe]::SetCursorPos($point.X,$point.Y)|Out-Null
   try{[GlassLifecycleProbe]::Click()}finally{[GlassLifecycleProbe]::ReleaseMouse()}
   break
  }
 }
 Wait-For { [GlassLifecycleProbe]::GetForegroundWindow()-eq$handle } ('Test window activation; target '+$handle+' ('+[GlassLifecycleProbe]::Title($handle)+'); actual '+[GlassLifecycleProbe]::ForegroundDescription())
}
function Top-WindowAtMain {
 $rect=Bounds $mainHandle
 $point=[GlassLifecycleProbe+Point]::new([int](($rect.Left+$rect.Right)/2),[int](($rect.Top+$rect.Bottom)/2))
 [GlassLifecycleProbe]::GetAncestor([GlassLifecycleProbe]::WindowFromPoint($point),2)
}
function Relaunch {
 $second=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$state+'"')) -PassThru
 if(!$second.WaitForExit(2500)){Stop-Process -Id $second.Id;throw 'A second app process stayed alive'}
 Wait-For {
  if(![GlassLifecycleProbe]::IsWindowVisible($mainHandle)-or![GlassLifecycleProbe]::IsWindowVisible($helperHandle)-or!(Same-Bounds)){return $false}
  $panel=Find-Main;if($null-eq$panel){return $false}
  $more=$panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'More'))
  return $null-ne$more-and!$more.Current.IsOffscreen
 } 'Resident expanded panel and glass restore together'
}
function Quit-TestApp {
 if(!$app.HasExited){
  $quitRequest=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$state+'"'),'--quit') -WindowStyle Hidden -PassThru
  if(!$quitRequest.WaitForExit(2500)){Stop-Process -Id $quitRequest.Id;throw 'The isolated quit request did not finish.'}
  if(!$app.WaitForExit(2500)){throw 'Quit did not exit the isolated app'}
 }
}
try {
 $app=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$state+'"')) -PassThru
 Wait-For {$script:root=Find-Main;$null-ne$script:root} 'Main UI startup' 5000
 $mainHandle=[IntPtr]$root.Current.NativeWindowHandle
 Wait-For {$script:helperHandle=Find-Helper;$script:helperHandle-ne[IntPtr]::Zero} 'Native glass helper startup' 5000
 Assert ([GlassLifecycleProbe]::GetWindow($mainHandle,4)-eq$helperHandle -and [GlassLifecycleProbe]::GetWindow($helperHandle,4)-eq[IntPtr]::Zero) 'Main window has exactly one independent glass owner'
 Wait-For {[GlassLifecycleProbe]::IsWindowVisible($helperHandle) -and (Same-Bounds)} 'Initial glass alignment'
 Assert-NormalLayer
 Assert-ExpandedRegion 'Initial expanded panel'
 $original=Bounds $mainHandle
 $backdropPath=Join-Path $vault 'GlassLifecycleBackdrop.exe'
 $backdropSource=Join-Path $vault 'GlassLifecycleBackdrop.cs'
 @'
using System;
using System.Drawing;
using System.Windows.Forms;
class LifecycleBackdrop : Form {
 LifecycleBackdrop(string[] args) {
  Text="To-do lifecycle ordinary window"; FormBorderStyle=FormBorderStyle.None;
  ShowInTaskbar=false; TopMost=false; StartPosition=FormStartPosition.Manual;
  Bounds=new Rectangle(int.Parse(args[0]),int.Parse(args[1]),int.Parse(args[2]),int.Parse(args[3]));
  BackColor=Color.FromArgb(188,204,216);
 }
 [STAThread] static void Main(string[] args){Application.Run(new LifecycleBackdrop(args));}
}
'@ | Set-Content -LiteralPath $backdropSource -Encoding UTF8
 & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ('/win32manifest:'+(Join-Path $project 'src\app.manifest')) ('/out:'+$backdropPath) $backdropSource
 if($LASTEXITCODE-ne 0){throw 'Controlled-window compilation failed'}
 $other=Start-Process -FilePath $backdropPath -ArgumentList @($original.Left,$original.Top,($original.Right-$original.Left),($original.Bottom-$original.Top),'light') -PassThru
 Wait-For {
  foreach($handle in [GlassLifecycleProbe]::Windows($other.Id)){if([GlassLifecycleProbe]::Class($handle)-like'WindowsForms10.Window*'){$script:otherHandle=$handle;return $true}}
  return $false
 } 'Other ordinary window startup'
 [GlassLifecycleProbe]::SetWindowPos($otherHandle,[IntPtr](-2),0,0,0,0,0x53)|Out-Null
 Activate-TestWindow $otherHandle
 Assert (([GlassLifecycleProbe]::GetWindowLongPtr($otherHandle,-20).ToInt64()-band 8)-eq 0) 'The overlapping test app is an ordinary non-topmost window'
 Assert ((Top-WindowAtMain)-eq$otherHandle) 'Activating another ordinary app covers To-do'
 Assert-NormalLayer
 Activate-TestWindow $mainHandle
 Assert ((Top-WindowAtMain)-eq$mainHandle) 'Activating To-do brings its content back in front'
 Assert-NormalLayer
 [GlassLifecycleProbe]::ShowWindow($otherHandle,0)|Out-Null
 [GlassLifecycleProbe]::SetWindowPos($mainHandle,[IntPtr]::Zero,($original.Left+34),($original.Top+22),($original.Right-$original.Left+46),($original.Bottom-$original.Top+30),0x14)|Out-Null
 Wait-For {Same-Bounds} 'Glass follows window move and resize'
 Assert (Same-Bounds) 'Moving and resizing keeps the glass aligned'
 Assert-ExpandedRegion 'Resized expanded panel'
 [GlassLifecycleProbe]::SetWindowPos($mainHandle,[IntPtr]::Zero,$original.Left,$original.Top,($original.Right-$original.Left),($original.Bottom-$original.Top),0x14)|Out-Null
 Wait-For {Same-Bounds} 'Reset glass alignment'
 Invoke-Control 'Collapse / Expand'
 Wait-For {$rect=Bounds $mainHandle;($rect.Right-$rect.Left)-le 64*[GlassLifecycleProbe]::GetDpiForWindow($mainHandle)/96.0+2} 'Collapse reaches bubble size'
 Start-Sleep -Milliseconds 320
 Wait-For {Same-Bounds} 'Docked bubble and glass alignment'
 Assert ([GlassLifecycleProbe]::IsWindowVisible($helperHandle)) 'Collapsed glass remains visible with the bubble'
 $mainRegion=[GlassLifecycleProbe]::CreateRectRgn(0,0,0,0);$helperRegion=[GlassLifecycleProbe]::CreateRectRgn(0,0,0,0)
 try {
  $mainKind=[GlassLifecycleProbe]::GetWindowRgn($mainHandle,$mainRegion);$helperKind=[GlassLifecycleProbe]::GetWindowRgn($helperHandle,$helperRegion)
  Assert ($mainKind-gt 1 -and $helperKind-gt 1 -and [GlassLifecycleProbe]::EqualRgn($mainRegion,$helperRegion)) 'Bubble foreground and glass use the same circular clip region'
  $scale=[GlassLifecycleProbe]::GetDpiForWindow($mainHandle)/96.0
  Assert (![GlassLifecycleProbe]::PtInRegion($helperRegion,[int](4*$scale),[int](12*$scale)) -and ![GlassLifecycleProbe]::PtInRegion($helperRegion,[int](59*$scale),[int](54*$scale)) -and [GlassLifecycleProbe]::PtInRegion($helperRegion,[int](28*$scale),[int](36*$scale)) -and [GlassLifecycleProbe]::PtInRegion($helperRegion,[int](54*$scale),[int](10*$scale))) 'Glass contains only the main circle and circular close button'
 }finally{[GlassLifecycleProbe]::DeleteObject($mainRegion)|Out-Null;[GlassLifecycleProbe]::DeleteObject($helperRegion)|Out-Null}
 Assert-BubbleLayer
 $bubbleBounds=Bounds $mainHandle
 [GlassLifecycleProbe]::SetWindowPos($otherHandle,[IntPtr](-2),($bubbleBounds.Left-30),($bubbleBounds.Top-30),($bubbleBounds.Right-$bubbleBounds.Left+60),($bubbleBounds.Bottom-$bubbleBounds.Top+60),0x50)|Out-Null
 Activate-TestWindow $otherHandle
 Assert ([GlassLifecycleProbe]::GetForegroundWindow()-eq$otherHandle) 'The ordinary overlapping app owns foreground while the bubble remains visible'
 Assert ((Top-WindowAtMain)-eq$mainHandle) 'Bubble remains above an activated ordinary app'
 Assert-BubbleLayer
 [GlassLifecycleProbe]::ShowWindow($otherHandle,0)|Out-Null
 Invoke-Control 'Close bubble'
 Wait-For {![GlassLifecycleProbe]::IsWindowVisible($mainHandle) -and ![GlassLifecycleProbe]::IsWindowVisible($helperHandle)} 'Both bubble windows hide together'
 Assert (!$app.HasExited) 'Closing bubble leaves the resident process alive'
 $saved=Get-Content (Join-Path $state 'settings.json') -Raw|ConvertFrom-Json
 Assert ($saved.Pinned-eq$false) 'Closing the topmost bubble does not save a pinned panel'
 Relaunch
 $restored=Bounds $mainHandle
 Assert ($restored.Left-eq$original.Left -and $restored.Top-eq$original.Top -and $restored.Right-eq$original.Right -and $restored.Bottom-eq$original.Bottom) ('Restore recovers full panel bounds and aligned glass (before '+$original.Left+','+$original.Top+','+$original.Right+','+$original.Bottom+'; after '+$restored.Left+','+$restored.Top+','+$restored.Right+','+$restored.Bottom+')')
 Assert-NormalLayer
 Assert-ExpandedRegion 'Restored expanded panel after circular bubble'
 Invoke-Control 'Close panel'
 Wait-For {![GlassLifecycleProbe]::IsWindowVisible($mainHandle) -and ![GlassLifecycleProbe]::IsWindowVisible($helperHandle)} 'Panel close hides the glass too'
 Relaunch
 [GlassLifecycleProbe]::ShowWindow($mainHandle,6)|Out-Null
 Wait-For {![GlassLifecycleProbe]::IsWindowVisible($helperHandle)} 'Minimization hides the glass'
 [GlassLifecycleProbe]::ShowWindow($mainHandle,9)|Out-Null
 Wait-For {[GlassLifecycleProbe]::IsWindowVisible($helperHandle) -and (Same-Bounds)} 'Unminimizing restores aligned glass'
 Assert-NormalLayer
 Quit-TestApp
 Assert (![GlassLifecycleProbe]::IsWindow($mainHandle) -and ![GlassLifecycleProbe]::IsWindow($helperHandle) -and @([GlassLifecycleProbe]::Windows($app.Id)).Count-eq 0) 'Quit removes the main window and every glass helper'
 $saved=Get-Content (Join-Path $state 'settings.json') -Raw|ConvertFrom-Json
 Assert ($saved.Pinned-eq$false) 'Lifecycle actions keep the user-selected normal window layer'
 $app=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$state+'"')) -PassThru
 Wait-For {$script:root=Find-Main;$null-ne$script:root} 'Restarted main UI startup' 5000
 $mainHandle=[IntPtr]$root.Current.NativeWindowHandle
 Wait-For {$script:helperHandle=Find-Helper;$script:helperHandle-ne[IntPtr]::Zero -and [GlassLifecycleProbe]::IsWindowVisible($script:helperHandle) -and (Same-Bounds)} 'Restarted native glass alignment' 5000
 Assert-NormalLayer
 Assert ((Bounds $mainHandle).Right-(Bounds $mainHandle).Left-gt 100) 'Full restart opens the expanded panel'
 Quit-TestApp
 Assert (![GlassLifecycleProbe]::IsWindow($mainHandle) -and ![GlassLifecycleProbe]::IsWindow($helperHandle)) 'Restarted app also closes both windows cleanly'
 Write-Output ('PASS '+$checkCount+' glass lifecycle assertions; isolated data: '+$vault)
}finally{
 [GlassLifecycleProbe]::ReleaseMouse()
 if($null-ne$other -and !$other.HasExited){Stop-Process -Id $other.Id}
 if($null-ne$app -and !$app.HasExited){
  try{Quit-TestApp}catch{Write-Warning ('Could not quit isolated lifecycle test gracefully: '+$_.Exception.Message)}
  if(!$app.HasExited){Stop-Process -Id $app.Id}
 }
 [GlassLifecycleProbe]::ReleaseMouse()
 [GlassLifecycleProbe]::SetCursorPos($originalCursor.X,$originalCursor.Y)|Out-Null
 if($originalForeground-ne[IntPtr]::Zero-and[GlassLifecycleProbe]::IsWindow($originalForeground)){[GlassLifecycleProbe]::Activate($originalForeground)|Out-Null}
}
