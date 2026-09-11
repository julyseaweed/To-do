param([string]$ApplicationPath,[switch]$FollowupOnly)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShikeCornerScaleInput {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
 [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
 [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
 [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] input,int size);
 public static void TypeMarker() {
  var down=new Input {Type=1};down.Data.Key=new KeyInput {Scan='X',Flags=4};
  var up=down;up.Data.Key.Flags=6;
  if(SendInput(2,new[]{down,up},Marshal.SizeOf(typeof(Input)))!=2)throw new InvalidOperationException("The test character could not be delivered.");
 }
}
'@
[ShikeCornerScaleInput]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
. (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ApplicationPath $ApplicationPath -HarnessOnly
$fixture="# Current`n`n## Watching list`n"
1..20|ForEach-Object{$fixture+="- [ ] [Watch $($_.ToString('00'))](<https://example.test/watch/$_>)`n"}
$fixture+="`n## Plans`n"
1..24|ForEach-Object{$fixture+="- [ ] Plan $($_.ToString('00'))`n"}
foreach($topic in @('Research','Ideas','Later')){$fixture+="`n## $topic`n- [ ] Item`n"}
[IO.File]::WriteAllText($inlineNote,$fixture,$utf8)
$settingsPath=Join-Path $inlineState 'settings.json'
$scaleSettings=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json
$scaleSettings.Pinned=$true;$scaleSettings.Left=40;$scaleSettings.Top=40;$scaleSettings.Width=960;$scaleSettings.Height=620
$scaleSettings|Add-Member -NotePropertyName InterfaceScale -NotePropertyValue 1 -Force
if($FollowupOnly){$scaleSettings.InterfaceScale=.8;$scaleSettings.Width=2+958*.8;$scaleSettings.Height=2+618*.8}
$scaleSettings|Add-Member -NotePropertyName LaunchLayout -NotePropertyValue ([pscustomobject]@{Left=$scaleSettings.Left;Top=$scaleSettings.Top;Width=$scaleSettings.Width;Height=$scaleSettings.Height;InterfaceScale=$scaleSettings.InterfaceScale}) -Force
[IO.File]::WriteAllText($settingsPath,($scaleSettings|ConvertTo-Json -Depth 4),$utf8)
function Corner-Rect {
 $r=[ShikeCornerScaleInput+Rect]::new()
 if(![ShikeCornerScaleInput]::GetWindowRect($inlineHandle,[ref]$r)){throw 'Cannot read isolated window bounds.'}
 [pscustomobject]@{Left=[double]$r.Left;Top=[double]$r.Top;Right=[double]$r.Right;Bottom=[double]$r.Bottom;Width=[double]($r.Right-$r.Left);Height=[double]($r.Bottom-$r.Top)}
}
function Corner-Scale { [double]((Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json).InterfaceScale) }
function Corner-Scroll([string]$Name) { (Find-InlineControl $Name).GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern) }
function Corner-ScrollSnapshot([string]$Name) {
 $state=(Corner-Scroll $Name).Current
 # ScrollPatternInformation is a live wrapper; freeze each value before resizing.
 [pscustomobject]@{VerticalScrollPercent=[double]$state.VerticalScrollPercent;VerticalViewSize=[double]$state.VerticalViewSize;HorizontalScrollPercent=[double]$state.HorizontalScrollPercent;HorizontalViewSize=[double]$state.HorizontalViewSize}
}
function Corner-Snapshot {
 $metrics=@{}
 foreach($name in @('More','Close panel','New topic','Rename Plans','Rename Watching list','Edit Plan 09','Edit Watch 06')){
  $control=Find-InlineControl $name
  if($null-eq$control){throw ('Missing scale metric: '+$name)}
  $metrics[$name]=$control.Current.BoundingRectangle
 }
 [pscustomobject]@{Rect=Corner-Rect;Scale=Corner-Scale;Metrics=$metrics;Plans=(Corner-ScrollSnapshot 'Items in Plans');Watching=(Corner-ScrollSnapshot 'Items in Watching list');Topics=(Corner-ScrollSnapshot 'Topics')}
}
function Corner-Move([double]$X,[double]$Y) {
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'The scaling fixture lost foreground; input stopped.'}
 [ShikeInlineInput]::SetCursorPos([int][Math]::Round($X),[int][Math]::Round($Y))|Out-Null
}
function Corner-Drag([string]$Corner,[double]$Factor,[switch]$Cancel) {
 Activate-Inline
 $r=Corner-Rect;$inset=8*$script:cornerDpi
 $left=$Corner.Contains('left');$top=$Corner.Contains('top')
 $x=if($left){$r.Left+$inset}else{$r.Right-$inset}
 $y=if($top){$r.Top+$inset}else{$r.Bottom-$inset}
 $hit=@{'top-left'=13;'top-right'=14;'bottom-left'=16;'bottom-right'=17}[$Corner]
 $packed=([long][Math]::Round($y)-shl16)-bor([long][Math]::Round($x)-band0xffff)
 $actual=[ShikeCornerScaleInput]::SendMessage($inlineHandle,0x84,[IntPtr]::Zero,[IntPtr]$packed).ToInt32()
 Assert-Inline ($actual-eq$hit) ($Corner+' exposes its native diagonal resize target')
 $point=[ShikeInlineInput+Point]::new();$point.X=[int]$x;$point.Y=[int]$y
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw 'The isolated resize point is covered; drag skipped.'}
 $dx=$r.Width*($Factor-1)*$(if($left){-1}else{1});$dy=$r.Height*($Factor-1)*$(if($top){-1}else{1})
 Corner-Move $x $y
 try{
  [ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 35
  for($step=1;$step-le12;$step++){Corner-Move ($x+$dx*$step/12) ($y+$dy*$step/12);Start-Sleep -Milliseconds 20}
  if($Cancel){Send-InlineKey 0x1B}
 }finally{[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 180
}
function Corner-Check($Before,$After,[string]$Corner,[double]$Factor) {
 $sx=$After.Rect.Width/$Before.Rect.Width;$sy=$After.Rect.Height/$Before.Rect.Height
 Assert-Inline ([Math]::Abs($sx-$Factor)-lt.025-and[Math]::Abs($sx-$sy)-lt.012) ($Corner+' changes both dimensions proportionally')
 $anchorX=if($Corner.Contains('left')){'Right'}else{'Left'};$anchorY=if($Corner.Contains('top')){'Bottom'}else{'Top'}
 Assert-Inline ([Math]::Abs($After.Rect.$anchorX-$Before.Rect.$anchorX)-le3-and[Math]::Abs($After.Rect.$anchorY-$Before.Rect.$anchorY)-le3) ($Corner+' holds the opposite corner in place')
 Assert-Inline ([Math]::Abs($After.Scale-$Before.Scale*$sx)-lt.025) ($Corner+' persists the proportional interface scale')
 foreach($name in $Before.Metrics.Keys){
  $a=$Before.Metrics[$name];$b=$After.Metrics[$name];$tolerance=[Math]::Max(3,$script:cornerDpi*1.5)
  Assert-Inline ([Math]::Abs($b.Width-$a.Width*$sx)-le$tolerance-and[Math]::Abs($b.Height-$a.Height*$sy)-le$tolerance) ($Corner+' scales the visible bounds of '+$name)
 }
 Assert-Inline ([Math]::Abs($After.Plans.VerticalScrollPercent-$Before.Plans.VerticalScrollPercent)-lt.3-and[Math]::Abs($After.Watching.VerticalScrollPercent-$Before.Watching.VerticalScrollPercent)-lt.3) ($Corner+' preserves both independent vertical scroll positions')
 Assert-Inline ([Math]::Abs($After.Plans.VerticalViewSize-$Before.Plans.VerticalViewSize)-lt.5-and[Math]::Abs($After.Watching.VerticalViewSize-$Before.Watching.VerticalViewSize)-lt.5-and[Math]::Abs($After.Topics.HorizontalViewSize-$Before.Topics.HorizontalViewSize)-lt.5) ($Corner+' preserves the visible portion of the content instead of cropping it')
}
function Corner-Edge([double]$Delta) {
 Activate-Inline;$r=Corner-Rect;$x=$r.Right-3*$script:cornerDpi;$y=$r.Top+$r.Height*.5
 Corner-Move $x $y
 try{[ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 35;for($step=1;$step-le10;$step++){Corner-Move ($x+$Delta*$step/10) $y;Start-Sleep -Milliseconds 20}}finally{[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 160
}
function Corner-ClickTyping {
 Activate-Inline
 $before=Corner-Snapshot;$saved=Read-InlineNote
 if([ShikeCornerScaleInput]::IsZoomed($inlineHandle)){throw 'The isolated fixture was unexpectedly maximized before the text click; input stopped.'}
 Assert-Inline ([Math]::Abs($before.Scale-.8)-lt.025) 'The pointer test runs with the interface scaled to 0.8'
 $target=Find-InlineControl 'Edit Plan 09'
 if($null-eq$target-or$target.Current.IsOffscreen){throw 'The scaled text target is not visible.'}
 $bounds=$target.Current.BoundingRectangle
 # Use a position within the first word, not the center of the much wider card.
 $x=$bounds.Left+16*$script:cornerDpi*$before.Scale;$y=$bounds.Top+$bounds.Height*.5
 $point=[ShikeInlineInput+Point]::new();$point.X=[int]$x;$point.Y=[int]$y
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw 'The scaled text target is covered; input skipped.'}
 $hit=[Windows.Automation.AutomationElement]::FromPoint([Windows.Point]::new($point.X,$point.Y))
 $hitNames=[Collections.Generic.List[string]]::new();$matchesTarget=$false
 for($depth=0;$null-ne$hit-and$depth-lt8;$depth++){
  $hitNames.Add($hit.Current.Name)
  if($hit.Current.Name-ceq'Edit Plan 09'){$matchesTarget=$true;break}
  $hit=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($hit)
 }
 ('Scaled pointer: window='+($before.Rect|ConvertTo-Json -Compress)+'; target='+$bounds.ToString()+'; point='+$point.X+','+$point.Y+'; hit='+($hitNames-join' > '))|Add-Content -LiteralPath (Join-Path $inlineVault 'scaled-pointer.txt')
 if(!$matchesTarget){throw ('The actual pointer hit differs from the scaled target; input stopped. Hit: '+($hitNames-join' > '))}
 Corner-Move $x $y
 Start-Sleep -Milliseconds 60
 try{[ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 70}finally{[ShikeInlineInput]::MouseButton($true)}
 Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq'Plan 09') 'A real pointer click opens the intended scaled card'
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Focus left the scaled editor; typing skipped.'}
 [ShikeCornerScaleInput]::TypeMarker()
 Wait-Inline {(Input-Value)-match'X'} 'Native typing did not reach the scaled editor.'
 $typed=Input-Value;$insertion=$typed.IndexOf('X')
 Assert-Inline ($insertion-gt0-and$insertion-lt7-and$typed.Remove($insertion,1)-ceq'Plan 09') 'Native typing inserts at the clicked position within scaled text'
 Click-Inline 'Edit Plan 10';Wait-InlineFocus 'Item text'
 Wait-Inline {(Input-Value)-ceq'Plan 10'-and(Read-InlineNote)-ceq$saved.Replace('- [ ] Plan 09','- [ ] '+$typed)} 'The scaled card switch did not save the typed edit.'
 Assert-Inline ((Input-Value)-ceq'Plan 10') 'A second scaled card remains clickable and saves the first edit'
 Send-InlineKey 0x1B
 Send-InlineKey 0x5A 0x11
 Wait-Inline {(Read-InlineNote)-ceq$saved} 'Undo did not cleanly restore the scaled typing fixture.'
 $after=Corner-Snapshot
 Assert-Inline ((Read-InlineNote)-ceq$saved-and[Math]::Abs($after.Plans.VerticalScrollPercent-$before.Plans.VerticalScrollPercent)-lt.3-and[Math]::Abs($after.Watching.VerticalScrollPercent-$before.Watching.VerticalScrollPercent)-lt.3) 'Undo removes the test edit while retaining both column scroll positions'
 $before=Corner-Snapshot;$oldProcess=$inlineApp.Id
 $launch=(Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json).LaunchLayout
 if($null-eq$launch){throw 'The fixed launch layout is missing before restart.'}
 Restart-InlineResident
 Start-Sleep -Milliseconds 180
 $after=Corner-Snapshot
 $persistedLaunch=(Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json).LaunchLayout
 Assert-Inline ($inlineApp.Id-ne$oldProcess-and$null-ne$persistedLaunch-and[Math]::Abs($persistedLaunch.InterfaceScale-$launch.InterfaceScale)-lt.001) 'A full process restart preserves the fixed launch scale preset'
 Assert-Inline ([Math]::Abs($after.Rect.Left-$launch.Left*$script:cornerDpi)-le3-and[Math]::Abs($after.Rect.Top-$launch.Top*$script:cornerDpi)-le3-and[Math]::Abs($after.Rect.Width-$launch.Width*$script:cornerDpi)-le3-and[Math]::Abs($after.Rect.Height-$launch.Height*$script:cornerDpi)-le3) 'A full process restart restores the fixed launch rectangle rather than the temporary resized layout'
 $launchFactor=$launch.InterfaceScale/$before.Scale
 Assert-Inline ([Math]::Abs($after.Metrics['More'].Height-$before.Metrics['More'].Height*$launchFactor)-le2-and[Math]::Abs($after.Metrics['Rename Plans'].Height-$before.Metrics['Rename Plans'].Height*$launchFactor)-le2) 'A full process restart renders controls and headings at the fixed launch scale'
}
try{
 $inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$null-ne(Get-InlineMain)} 'The isolated scaling fixture did not open.'
 Activate-Inline;$script:cornerDpi=[ShikeCornerScaleInput]::GetDpiForWindow($inlineHandle)/96.0
 (Corner-Scroll 'Items in Plans').SetScrollPercent(-1,30)
 (Corner-Scroll 'Items in Watching list').SetScrollPercent(-1,20)
 Start-Sleep -Milliseconds 150
 if(!$FollowupOnly){
 foreach($corner in @('top-left','top-right','bottom-left','bottom-right')){
  $before=Corner-Snapshot
  Corner-Drag $corner .8
  $small=Corner-Snapshot;Corner-Check $before $small $corner .8
  if($corner-eq'top-left'){& (Join-Path $PSScriptRoot 'Inspect-Window.ps1') -AppId $inlineApp.Id -OutputPath (Join-Path $inlineVault 'corner-shrunk.png') -RequireActive}
  Corner-Drag $corner (1/.8)
  Corner-Check $small (Corner-Snapshot) $corner (1/.8)
 }
 $before=Corner-Snapshot;Corner-Edge (-80*$script:cornerDpi);$after=Corner-Snapshot
 Assert-Inline ($after.Rect.Width-lt$before.Rect.Width-50*$script:cornerDpi-and[Math]::Abs($after.Rect.Height-$before.Rect.Height)-le2) 'A side edge resizes only the window width'
 Assert-Inline ([Math]::Abs($after.Scale-$before.Scale)-lt.001-and[Math]::Abs($after.Metrics['More'].Height-$before.Metrics['More'].Height)-le2-and[Math]::Abs($after.Metrics['Rename Plans'].Height-$before.Metrics['Rename Plans'].Height)-le2) 'A side-edge resize preserves title and control scale'
 Corner-Edge ($before.Rect.Width-$after.Rect.Width)
 $before=Corner-Snapshot;Corner-Drag 'bottom-right' .8 -Cancel;$after=Corner-Snapshot
 Assert-Inline ([Math]::Abs($after.Rect.Width-$before.Rect.Width)-le3-and[Math]::Abs($after.Rect.Height-$before.Rect.Height)-le3-and[Math]::Abs($after.Scale-$before.Scale)-lt.01) 'Escape restores both the original size and interface scale'
 Corner-Drag 'bottom-right' .8;$before=Corner-Snapshot
 Invoke-Inline 'Collapse / Expand'
 Wait-Inline {$r=Corner-Rect;[Math]::Abs($r.Width-64*$script:cornerDpi)-lt3-and[Math]::Abs($r.Height-64*$script:cornerDpi)-lt3} 'The collapsed bubble did not retain its fixed 64 DIP size.'
 Assert-Inline ($true) 'The bubble keeps its fixed size independently of interface scale'
 Invoke-Inline 'Open list';Start-Sleep -Milliseconds 180;$after=Corner-Snapshot
 Assert-Inline ([Math]::Abs($after.Rect.Width-$before.Rect.Width)-le3-and[Math]::Abs($after.Rect.Height-$before.Rect.Height)-le3-and[Math]::Abs($after.Scale-$before.Scale)-lt.01) 'Expanding restores the scaled panel size'
 Assert-Inline ([Math]::Abs($after.Metrics['More'].Height-$before.Metrics['More'].Height)-le2-and[Math]::Abs($after.Plans.VerticalScrollPercent-$before.Plans.VerticalScrollPercent)-lt.3) 'Expanding retains content scale and scroll position'
 }
 Corner-ClickTyping
 Assert-Inline ((Read-InlineNote)-ceq$fixture) 'Corner and edge resizing do not modify notes'
 'PASS '+$passCount+' native corner scaling checks; isolated data: '+$inlineVault
}finally{Stop-InlineHarness}
