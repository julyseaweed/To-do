param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
$automationReferences=@([System.Windows.Automation.AutomationElement].Assembly.Location,[System.Windows.Automation.AutomationProperty].Assembly.Location,[System.Windows.DependencyObject].Assembly.Location)
if($PSVersionTable.PSEdition-eq'Core'){$automationReferences+=@('System.Threading.dll','System.Threading.Thread.dll')}
Add-Type -ReferencedAssemblies $automationReferences @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
public static class DragRefreshProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 // A blocked provider must not prevent the driver from releasing the mouse.
 public static bool Contains(IntPtr handle,string name,int timeoutMs) {
  bool result=false;Exception failure=null;
  var done=new ManualResetEvent(false);
  var worker=new Thread(delegate(){
   try {var root=AutomationElement.FromHandle(handle);result=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,name))!=null;}
   catch(Exception ex){failure=ex;}
   finally{done.Set();}
  });
  worker.IsBackground=true;worker.Start();
  if(!done.WaitOne(timeoutMs))throw new TimeoutException("UIAutomation did not answer while inspecting "+name);
  done.Dispose();
  if(failure!=null)throw failure;
  return result;
 }
}
'@
[DragRefreshProbe]::SetProcessDPIAware()|Out-Null
$vault=Join-Path $env:TEMP ('shike-drag-refresh-'+[Guid]::NewGuid().ToString('N'))
$state=Join-Path $vault 'state';New-Item -ItemType Directory -Path $state -Force|Out-Null
$notePath=Join-Path $vault '当前.md'
[System.IO.File]::WriteAllText($notePath,"# Current`n`n## To read`n",[System.Text.UTF8Encoding]::new($false))
@{Vault=$vault;DesignVersion=2;Left=16;Top=520;Width=520;Height=300;Transparency=45;Pinned=$false;Zoom=1}|ConvertTo-Json|Set-Content (Join-Path $state 'settings.json')
$app=$null;$mainHandle=[IntPtr]::Zero
$cursor=[DragRefreshProbe+Point]::new();[DragRefreshProbe]::GetCursorPos([ref]$cursor)|Out-Null
function Assert($condition,$label){if(!$condition){throw $label};Write-Output ('PASS '+$label)}
function Bounds {$rect=[DragRefreshProbe+Rect]::new();[DragRefreshProbe]::GetWindowRect($mainHandle,[ref]$rect)|Out-Null;return $rect}
function Check-App {
 if($app.HasExited){throw 'Isolated test app exited unexpectedly'}
 $errorPath=Join-Path $state 'error.log';if(Test-Path $errorPath){throw(Get-Content $errorPath -Raw)}
}
function Contains([string]$name){[DragRefreshProbe]::Contains($mainHandle,$name,800)}
function Wait-Visible([string]$name){
 $watch=[System.Diagnostics.Stopwatch]::StartNew()
 do {Check-App;if(Contains $name){return $watch.ElapsedMilliseconds};Start-Sleep -Milliseconds 80}while($watch.ElapsedMilliseconds-lt 2300)
 throw ('External edit did not appear after release: '+$name)
}
function Mouse-Up {[DragRefreshProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
function Move-Held([int]$x,[int]$y){[DragRefreshProbe]::SetCursorPos($x,$y)|Out-Null;Start-Sleep -Milliseconds 100}
function Hold-ThroughRefresh([string]$newAction,[int]$x,[int]$y){
 $watch=[System.Diagnostics.Stopwatch]::StartNew()
 do {
  Check-App
  [DragRefreshProbe]::SetCursorPos(($x+[int](($watch.ElapsedMilliseconds/160)%2)),$y)|Out-Null
  Start-Sleep -Milliseconds 180
  Assert (([DragRefreshProbe]::GetAsyncKeyState(1)-band 0x8000)-ne 0) 'Pointer remains held during the interaction'
  Assert (!(Contains $newAction)) 'External edit remains outside the UI until mouse release'
 }while($watch.ElapsedMilliseconds-lt 1550)
}
try {
 # Passing --vault intentionally makes this isolated geometry test non-resident;
 # its X exits it, without touching the user's resident app or notes.
 $app=Start-Process -FilePath $ApplicationPath -ArgumentList @('--vault',('"'+$vault+'"'),'--state-dir',('"'+$state+'"')) -PassThru
 $root=$null
 for($attempt=0;$attempt-lt 50;$attempt++){Start-Sleep -Milliseconds 100;Check-App;$root=Get-ShikeWindow -AppId $app.Id;if($null-ne$root){break}}
 Assert ($null-ne$root) 'Isolated empty list starts'
 $mainHandle=[IntPtr]$root.Current.NativeWindowHandle
 $scale=[DragRefreshProbe]::GetDpiForWindow($mainHandle)/96.0
 [DragRefreshProbe]::SetWindowPos($mainHandle,[IntPtr]::Zero,0,0,0,0,0x43)|Out-Null
 [DragRefreshProbe]::SetForegroundWindow($mainHandle)|Out-Null
 Start-Sleep -Milliseconds 250
 Assert ([DragRefreshProbe]::GetForegroundWindow()-eq$mainHandle) 'Only the isolated test window receives the mouse interaction'
 Assert (!(Contains 'Complete During drag')) 'Empty fixture has no pending drag item'
 $original=Bounds
 # The middle of the 60-DIP header avoids its buttons, icon, and resize corners.
 $x=[int]($original.Left+270*$scale);$y=[int]($original.Top+30*$scale)
 [DragRefreshProbe]::SetCursorPos($x,$y)|Out-Null
 [DragRefreshProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 $x+=48;$y+=24;Move-Held $x $y
 $moving=Bounds
 Assert ([Math]::Abs($moving.Left-$original.Left)-ge 30 -and [Math]::Abs($moving.Top-$original.Top)-ge 12) 'Real header drag moves the window while the mouse is held'
 [System.IO.File]::WriteAllText($notePath,"# Current`n`n## To read`n- [ ] During drag`n",[System.Text.UTF8Encoding]::new($false))
 Hold-ThroughRefresh 'Complete During drag' $x $y
 Mouse-Up
 $elapsed=Wait-Visible 'Complete During drag'
 Assert ($elapsed-le 2300) ('External drag edit appears after release in '+$elapsed+' ms')
 $beforeResize=Bounds
 $x=[int]($beforeResize.Right-14*$scale);$y=[int]($beforeResize.Bottom-14*$scale)
 [DragRefreshProbe]::SetCursorPos($x,$y)|Out-Null
 [DragRefreshProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 $x+=48;$y+=40;Move-Held $x $y
 $resizing=Bounds
 Assert (($resizing.Right-$resizing.Left)-gt($beforeResize.Right-$beforeResize.Left)+25 -and ($resizing.Bottom-$resizing.Top)-gt($beforeResize.Bottom-$beforeResize.Top)+20) 'Real corner drag resizes both dimensions while the mouse is held'
 [System.IO.File]::AppendAllText($notePath,"- [ ] During resize`n",[System.Text.UTF8Encoding]::new($false))
 Hold-ThroughRefresh 'Complete During resize' $x $y
 Mouse-Up
 $elapsed=Wait-Visible 'Complete During resize'
 Assert ($elapsed-le 2300) ('External resize edit appears after release in '+$elapsed+' ms')
 [System.IO.File]::AppendAllText($notePath,"- [ ] After interaction`n",[System.Text.UTF8Encoding]::new($false))
 $elapsed=Wait-Visible 'Complete After interaction'
 Assert ($elapsed-le 2300 -and (Contains 'Complete During drag') -and (Contains 'Complete During resize')) 'Normal external refresh continues afterward and preserves both deferred edits'
 Check-App
}finally{
 Mouse-Up
 [DragRefreshProbe]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
 if($null-ne$app -and !$app.HasExited){
  try {
   $root=Get-ShikeWindow -AppId $app.Id
   $close=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Close panel'))
   if($null-ne$close){$close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();$app.WaitForExit(2000)|Out-Null}
  }catch{Write-Warning ('Isolated test close failed: '+$_.Exception.Message)}
  if(!$app.HasExited){Stop-Process -Id $app.Id}
 }
}
