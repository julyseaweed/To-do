param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
if(!$ApplicationPath){$ApplicationPath=Join-Path $project 'artifacts\To-do.exe'}
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LoginStartupProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
 [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
[LoginStartupProbe]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
$fixture=Join-Path $project ('artifacts\teststate-'+[Guid]::NewGuid().ToString('N'))
$state=Join-Path $fixture 'state'
New-Item -ItemType Directory -Path $state -Force|Out-Null
$settingsPath=Join-Path $state 'settings.json'
$initial=@(100,80,540,320);$interfaceScale=.823;$utf8=[Text.UTF8Encoding]::new($false)
$preset=@{Left=100;Top=80;Width=540;Height=320;InterfaceScale=$interfaceScale}
# Deliberately different transient geometry proves launch chooses the preset.
@{Vault=$fixture;DesignVersion=2;Left=220;Top=160;Width=620;Height=360;InterfaceScale=1.1;LaunchLayout=$preset;Transparency=73;Pinned=$false;Zoom=1.2;WatchingTitle='Watch later';WatchingWidth=250}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $settingsPath
$notePath=Join-Path $fixture ([string][char]0x5F53+[char]0x524D+'.md')
$noteBefore="# Current`n`n## Watching list`n- [ ] [Saved reference](<https://example.test/reference>)`n`n## Plans`n- [ ] Keep this task`n"
[IO.File]::WriteAllText($notePath,$noteBefore,$utf8)
$startupKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupBefore=(Get-ItemProperty -LiteralPath $startupKey -Name 'To-do' -ErrorAction SilentlyContinue).'To-do'
$foregroundBefore=[LoginStartupProbe]::GetForegroundWindow()
$script:app=$null;$script:handle=[IntPtr]::Zero;$script:root=$null;$script:checks=0
function Assert($condition,[string]$label){if(!$condition){throw $label};$script:checks++;Write-Output ('PASS '+$label)}
function Wait-For([scriptblock]$predicate,[string]$label){
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do{
  $errorLog=Join-Path $state 'error.log';if(Test-Path -LiteralPath $errorLog){throw [IO.File]::ReadAllText($errorLog)}
  if(&$predicate){return};Start-Sleep -Milliseconds 60
 }while($clock.ElapsedMilliseconds-lt5000)
 throw ('Timed out: '+$label+'; isolated data: '+$fixture)
}
function Current-Bounds {
 $r=[LoginStartupProbe+Rect]::new()
 if(![LoginStartupProbe]::GetWindowRect($script:handle,[ref]$r)){throw 'Cannot read the isolated window rectangle.'}
 @([double]$r.Left,[double]$r.Top,[double]($r.Right-$r.Left),[double]($r.Bottom-$r.Top))
}
function Bounds-Match([double[]]$actual,[double[]]$expected,[double]$tolerance=1.1){
 for($i=0;$i-lt4;$i++){if([Math]::Abs($actual[$i]-$expected[$i])-gt$tolerance){return $false}};return $true
}
function Physical([double[]]$dips){@($dips|ForEach-Object{[Math]::Round($_*$script:dpi)})}
function Saved-Matches([double[]]$expected){
 try{$saved=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json}catch{return $false}
 (Bounds-Match @($saved.Left,$saved.Top,$saved.Width,$saved.Height) $expected (1.1/$script:dpi))-and[Math]::Abs($saved.InterfaceScale-$interfaceScale)-lt.0001
}
function Preset-Matches {
 try{$saved=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json}catch{return $false}
 $layout=$saved.LaunchLayout
 $null-ne$layout-and(Bounds-Match @($layout.Left,$layout.Top,$layout.Width,$layout.Height) $initial .00001)-and[Math]::Abs($layout.InterfaceScale-$interfaceScale)-lt.00001
}
function Work-Bounds {
 $info=[LoginStartupProbe+MonitorInfo]::new();$info.Size=[Runtime.InteropServices.Marshal]::SizeOf($info)
 if(![LoginStartupProbe]::GetMonitorInfo([LoginStartupProbe]::MonitorFromWindow($script:handle,2),[ref]$info)){throw 'Cannot read the fixture monitor work area.'}
 $info.Work
}
function Start-TestApp([bool]$autostart){
 $argsList=@('--state-dir',('"'+$state+'"'));if($autostart){$argsList+='--autostart'}
 $script:app=Start-Process -FilePath $ApplicationPath -ArgumentList $argsList -PassThru
 Wait-For {$script:root=Get-ShikeWindow -AppId $script:app.Id;$null-ne$script:root-and$script:root.Current.BoundingRectangle.Width-gt100} 'Isolated app startup'
 $script:handle=[IntPtr]$script:root.Current.NativeWindowHandle
 $script:dpi=[LoginStartupProbe]::GetDpiForWindow($script:handle)/96.0
}
function Invoke-Control([string]$name){
 $main=Get-ShikeWindow -AppId $script:app.Id
 $control=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))
 if($null-eq$control){throw ('Missing isolated control: '+$name)}
 $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Find-Menu([string]$name){
 $condition=[System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$script:app.Id),[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name))
 [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Relaunch-TestApp([bool]$autostart=$false){
 $argsList=@('--state-dir',('"'+$state+'"'));if($autostart){$argsList+='--autostart'}
 $second=Start-Process -FilePath $ApplicationPath -ArgumentList $argsList -PassThru
 if(!$second.WaitForExit(2500)){Stop-Process -Id $second.Id;throw 'A second resident instance did not exit.'}
 Wait-For {[LoginStartupProbe]::IsWindowVisible($script:handle)-and(Current-Bounds)[2]-gt200} 'Resident panel restore'
}
function Quit-TestApp {
 if($null-eq$script:app-or$script:app.HasExited){return}
 $quitRequest=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$state+'"'),'--quit') -WindowStyle Hidden -PassThru
 if(!$quitRequest.WaitForExit(2500)){Stop-Process -Id $quitRequest.Id;throw 'The isolated quit request did not finish.'}
 if(!$script:app.WaitForExit(2500)){throw 'The isolated app did not exit after --quit.'}
}
function Check-More {
 Invoke-Control 'More'
 Wait-For {$null-ne(Find-Menu 'Open at login')} 'Open at login menu'
 Assert ($null-ne(Find-Menu 'Open at login')) 'Open at login remains available in More'
 $quit=Find-Menu 'Quit'
 Assert ($null-eq$quit-or$quit.Current.IsOffscreen) 'More contains no Quit action'
}
function Manual-Layout([double[]]$expected,[string]$kind){
 $physical=Physical $expected;$foreground=[LoginStartupProbe]::GetForegroundWindow()
 [LoginStartupProbe]::SendMessage($script:handle,0x231,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null
 try{
  if(![LoginStartupProbe]::SetWindowPos($script:handle,[IntPtr]::Zero,[int]$physical[0],[int]$physical[1],[int]$physical[2],[int]$physical[3],0x14)){throw 'The isolated geometry change failed.'}
  Wait-For {Bounds-Match (Current-Bounds) $physical} ('Native '+$kind+' geometry')
 }finally{[LoginStartupProbe]::SendMessage($script:handle,0x232,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null}
 # No close, activation or deactivation occurs before this disk assertion.
 Wait-For {Saved-Matches $expected} ('Immediate settings save at the end of '+$kind)
 Assert ((Saved-Matches $expected)-and(Preset-Matches)-and[LoginStartupProbe]::GetForegroundWindow()-eq$foreground) ($kind+' saves transient bounds without changing the fixed launch preset or focus')
}
function Assert-Layout([double[]]$expected,[string]$label){
 # Geometry can settle just before the durable settings replacement finishes.
 # Wait for both and keep that observation, rather than racing a second read.
 $script:layoutVerified=$false
 Wait-For {$script:layoutVerified=(Bounds-Match (Current-Bounds) (Physical $expected))-and(Preset-Matches);$script:layoutVerified} $label
 Assert $script:layoutVerified $label
}
function Heading-Height {
 $heading=(Get-ShikeWindow -AppId $script:app.Id).FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Rename Plans'))
 if($null-eq$heading){throw 'The scale comparison heading is missing.'}
 [double]$heading.Current.BoundingRectangle.Height
}
try {
 Start-TestApp $false
 Assert-Layout $initial 'A desktop launch uses fixed preset bounds instead of the saved transient rectangle'
 $titleHeight=Heading-Height
 Manual-Layout @(140,110,540,320) 'Moving the panel'
 $manual=@(140,110,580,350)
 Manual-Layout $manual 'Resizing the panel'
 Assert ([Math]::Abs((Heading-Height)-$titleHeight)-le1.1) 'Transient movement and resizing retain the interface scale'
 Invoke-Control 'Close panel';Wait-For {![LoginStartupProbe]::IsWindowVisible($script:handle)} 'Hide the moved panel'
 Relaunch-TestApp
 Assert-Layout $initial 'Opening the desktop icon after hiding returns to the fixed preset'
 Manual-Layout $manual 'A second temporary layout'
 Quit-TestApp
 Start-TestApp $false
 Assert-Layout $initial 'A full normal restart uses the fixed preset'
 Assert ([Math]::Abs((Heading-Height)-$titleHeight)-le1.1) 'A normal restart restores the preset rendered scale'
 Check-More
 Quit-TestApp
 Start-TestApp $true
 Assert-Layout $initial 'Open at login uses the same fixed preset'
 Assert ([Math]::Abs((Heading-Height)-$titleHeight)-le1.1) 'Open at login restores the preset rendered scale'
 foreach($launch in 1..2){
  Manual-Layout $manual ('Temporary layout before autostart '+$launch)
  $firstProcess=$script:app.Id
  Relaunch-TestApp $true
  Assert-Layout $initial ('Repeated autostart '+$launch+' reapplies the fixed preset')
  Assert ($script:app.Id-eq$firstProcess-and!$script:app.HasExited) ('Repeated autostart '+$launch+' reuses the resident process')
 }
 Manual-Layout $manual 'The temporary layout before collapsing'
 Invoke-Control 'Collapse / Expand'
 Wait-For {(Current-Bounds)[2]-le65*$script:dpi} 'Collapse the temporary panel'
 Start-Sleep -Milliseconds 300
 $work=Work-Bounds;$bubble=Current-Bounds
 Assert ([Math]::Abs($bubble[0]+$bubble[2]-($work.Right-12*$script:dpi))-le2) 'Collapsing initially docks the bubble at the right edge'
 $bubbleX=$work.Left+180*$script:dpi;$bubbleY=$work.Top+140*$script:dpi
 [LoginStartupProbe]::SetWindowPos($script:handle,[IntPtr]::Zero,[int]$bubbleX,[int]$bubbleY,0,0,0x15)|Out-Null
 Start-Sleep -Milliseconds 300
 $bubble=Current-Bounds
 Assert ([Math]::Abs($bubble[0]-$bubbleX)-le2-and[Math]::Abs($bubble[1]-$bubbleY)-le2) 'A moved bubble stays at its chosen location'
 Assert (Preset-Matches) 'Moving the bubble leaves the launch preset unchanged'
 Invoke-Control 'Open list'
  $nearBubble=@(($bubbleX/$script:dpi),($bubbleY/$script:dpi),$manual[2],$manual[3])
 Assert-Layout $nearBubble 'Clicking the bubble expands the temporary panel beside its current location'
 Relaunch-TestApp
 Assert-Layout $initial 'The desktop icon returns a bubble-expanded panel to the fixed preset'
 Invoke-Control 'Collapse / Expand'
 Wait-For {(Current-Bounds)[2]-le65*$script:dpi} 'Collapse again'
 Start-Sleep -Milliseconds 300
 $bubble=Current-Bounds
 Assert ([Math]::Abs($bubble[0]+$bubble[2]-($work.Right-12*$script:dpi))-le2) 'The next collapse docks at the right edge again'
 Invoke-Control 'Close bubble';Wait-For {![LoginStartupProbe]::IsWindowVisible($script:handle)} 'Hide the bubble'
 Assert (Preset-Matches) 'Closing the bubble leaves the launch preset unchanged'
 Relaunch-TestApp
 Assert-Layout $initial 'The desktop icon restores the fixed preset from a hidden bubble'
 Invoke-Control 'Collapse / Expand';Wait-For {(Current-Bounds)[2]-le65*$script:dpi} 'Collapse before graceful exit'
 Quit-TestApp
 Start-TestApp $true
 Assert-Layout $initial 'Login after quitting while collapsed opens the fixed expanded preset'
 Assert ([Math]::Abs((Heading-Height)-$titleHeight)-le1.1) 'Login after a collapsed exit restores the preset rendered scale'
 Check-More
 Quit-TestApp
 $saved=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json
 Assert (Preset-Matches) 'No transient move, resize, collapse or exit changes the fixed launch preset'
 Assert ($saved.Pinned-eq$false-and$saved.Transparency-eq73-and$saved.Zoom-eq1.2-and$saved.DesignVersion-eq2-and$saved.Vault-eq$fixture-and$saved.WatchingTitle-ceq'Watch later'-and$saved.WatchingWidth-eq250) 'Launch behavior preserves every unrelated preference'
 Assert ([IO.File]::ReadAllText($notePath)-ceq$noteBefore) 'Launch and bubble behavior leave note content unchanged'
 $startupAfter=(Get-ItemProperty -LiteralPath $startupKey -Name 'To-do' -ErrorAction SilentlyContinue).'To-do'
 Assert ($startupAfter-ceq$startupBefore) 'The global Open at login registration remains unchanged'
 'PASS '+$script:checks+' fixed launch layout checks; isolated data: '+$fixture

}finally{
 if($null-ne$script:app-and!$script:app.HasExited){try{Quit-TestApp}catch{Write-Warning $_.Exception.Message};if(!$script:app.HasExited){Stop-Process -Id $script:app.Id}}
 if($foregroundBefore-ne[IntPtr]::Zero-and[LoginStartupProbe]::IsWindow($foregroundBefore)){[LoginStartupProbe]::SetForegroundWindow($foregroundBefore)|Out-Null}
}
