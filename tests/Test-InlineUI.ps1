param([string]$ApplicationPath, [switch]$ValidateOnly, [switch]$ConflictOnly, [switch]$HarnessOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
if (-not ('ShikeInlineInput' -as [type])) {
 Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ShikeInlineInput {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
 [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
 [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
 [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
 delegate bool EnumProc(IntPtr hwnd,IntPtr unused);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr unused);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint current,uint foreground,bool attach);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
 [DllImport("user32.dll")] static extern uint SendInput(uint count,Input[] inputs,int size);
 public static bool Activate(IntPtr hwnd) {
  if(GetForegroundWindow()==hwnd)return true;
  uint ignored;uint current=GetCurrentThreadId();uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);
  bool attached=foreground!=0&&foreground!=current&&AttachThreadInput(current,foreground,true);
  try { SetForegroundWindow(hwnd);return GetForegroundWindow()==hwnd; }
  finally { if(attached)AttachThreadInput(current,foreground,false); }
 }
 public static bool ForegroundBelongsTo(int processId) { uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);return id==processId; }
 public static uint ForegroundProcessId() { uint id;GetWindowThreadProcessId(GetForegroundWindow(),out id);return id; }
 public static void Key(ushort key,bool up) {
  var input=new Input {Type=1};input.Data.Key=new KeyInput {Key=key,Flags=up?2u:0u};
  if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1)throw new InvalidOperationException("Keyboard input could not be delivered.");
 }
 public static void MouseButton(bool up) {
  var input=new Input {Type=0};input.Data.Mouse=new MouseInput {Flags=up?4u:2u};
  if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1)throw new InvalidOperationException("Mouse input could not be delivered.");
 }
 public static void MouseWheel(int delta) {
  var input=new Input {Type=0};input.Data.Mouse=new MouseInput {Flags=0x0800,Data=unchecked((uint)delta)};
  if(SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))!=1)throw new InvalidOperationException("Mouse wheel input could not be delivered.");
 }
 public static void ReleaseInputs() {
  foreach(ushort key in new ushort[]{0x0D,0x1B,0x09,0x08,0x2E,0x10,0x11,0x12,0x20,0x41,0x79,0x5A})try{Key(key,true);}catch{}
  var input=new Input {Type=0};input.Data.Mouse=new MouseInput {Flags=0x04|0x10|0x40};SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)));
 }
 public static string[] VisibleTitles(int processId) {
  var titles=new List<string>();
  EnumWindows(delegate(IntPtr hwnd,IntPtr unused){
   uint id;GetWindowThreadProcessId(hwnd,out id);if(id!=processId||!IsWindowVisible(hwnd))return true;
   var title=new StringBuilder(256);GetWindowText(hwnd,title,title.Capacity);if(title.Length>0)titles.Add(title.ToString());return true;
  },IntPtr.Zero);return titles.ToArray();
 }
}
'@
}
if ($ValidateOnly) { 'PASS Inline UI script parses and native input helper compiles; no application opened'; return }
if (!$ApplicationPath) { $ApplicationPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\To-do.exe' }
$ApplicationPath=(Resolve-Path -LiteralPath $ApplicationPath).Path
$inlineVault=Join-Path $env:TEMP ('shike-inline-ui-'+[Guid]::NewGuid().ToString('N'))
$inlineState=Join-Path $inlineVault 'state'
$inlineNote=Join-Path $inlineVault ([string][char]0x5F53+[char]0x524D+'.md')
$utf8=[System.Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path $inlineState -Force | Out-Null
$fixture="# Current`n`n## Existing`n- [ ] [Reference](<https://example.com/keep?x=1&y=2>)`n  > Keep this note.`n- [ ] Following`n`n## Watching list`n"
if($ConflictOnly){
 $fixture="# Current`n`n## Renamed`n- [ ] [Reference revised](<https://example.org/updated>)`n  > Keep this note.`n- [ ] Following`n`n## Plans`n- [ ] First revised`n- [ ] Inserted between`n- [ ]`n- [ ] Second item`n- [ ] Saved on close`n`n## Watching list`n"
}
[IO.File]::WriteAllText($inlineNote,$fixture,$utf8)
$settings=@{Vault=$inlineVault;DesignVersion=2;Left=16;Top=16;Width=960;Height=620;Transparency=45;Pinned=$false;Zoom=1}|ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $inlineState 'settings.json'),$settings,$utf8)
$inlineApp=$null
$inlineHandle=[IntPtr]::Zero
$originalForeground=[ShikeInlineInput]::GetForegroundWindow()
$originalCursor=[ShikeInlineInput+Point]::new()
[ShikeInlineInput]::GetCursorPos([ref]$originalCursor)|Out-Null
$passCount=0

function Assert-Inline([bool]$Condition,[string]$Message) {
 if(!$Condition){Save-InlineFailure $Message;throw $Message}
 $script:passCount++;'PASS '+$Message
}
function Save-InlineFailure([string]$Message) {
 $diagnostics=[Collections.Generic.List[string]]::new()
 $foregroundId=[ShikeInlineInput]::ForegroundProcessId();$foregroundName=(Get-Process -Id $foregroundId -ErrorAction SilentlyContinue).ProcessName
 $diagnostics.Add('Foreground: '+$foregroundName+' PID '+$foregroundId+'; expected isolated PID '+$inlineApp.Id)
 $diagnostics.Add($Message+'; Item text: '+(Input-Value)+'; Item link: '+(Input-Value 'Item link'))
 foreach($label in @('Items in Watching list','Items in Plans','Topics','Column Watching list','Column Plans','Input error')){
  $element=Find-InlineControl $label
  if($null-eq$element){continue}
  $diagnostics.Add($label+': bounds='+$element.Current.BoundingRectangle.ToString()+'; offscreen='+$element.Current.IsOffscreen)
  try{$state=$element.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current;$diagnostics.Add('  VerticalPercent='+$state.VerticalScrollPercent+'; VerticalView='+$state.VerticalViewSize+'; Scrollable='+$state.VerticallyScrollable+'; HorizontalPercent='+$state.HorizontalScrollPercent)}catch{}
 }
 $controls=(Get-InlineMain).FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
 $diagnostics.Add('Controls: '+(($controls|ForEach-Object{$_.Current.Name}|Where-Object{$_-match'^(Edit|Remove|Complete|Item|Input)'})-join' | '))
 $diagnostics.Add('Note: '+(Read-InlineNote))
 $diagnosticPath=Join-Path $inlineVault 'ui-failure.txt'
 [IO.File]::WriteAllLines($diagnosticPath,$diagnostics,$utf8)
 Write-Output ($diagnostics-join[Environment]::NewLine)
 try{& (Join-Path $PSScriptRoot 'Inspect-Window.ps1') -AppId $inlineApp.Id -OutputPath (Join-Path $inlineVault 'ui-failure.png') -RequireActive}catch{Write-Warning ('Failure screenshot: '+$_.Exception.Message)}
 Write-Output ('Failure diagnostics: '+$diagnosticPath)
}
function Get-InlineMain { Get-ShikeWindow -AppId $inlineApp.Id }
function Find-InlineControl([string]$Name,[System.Windows.Automation.ControlType]$Type=$null,[switch]$Anywhere) {
 $conditions=[System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
 $conditions.Add([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$Name))
 if($Type){$conditions.Add([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$Type))}
 if($Anywhere){$conditions.Add([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$inlineApp.Id));$root=[System.Windows.Automation.AutomationElement]::RootElement}else{$root=Get-InlineMain}
 if($null-eq$root){return $null}
 $condition=if($conditions.Count-eq 1){$conditions[0]}else{[System.Windows.Automation.AndCondition]::new($conditions.ToArray())}
 $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Wait-Inline([scriptblock]$Condition,[string]$Message,[int]$Timeout=5000) {
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do {
  $errorPath=Join-Path $inlineState 'error.log'
  if(Test-Path -LiteralPath $errorPath){throw [IO.File]::ReadAllText($errorPath)}
  $inlineApp.Refresh();if($inlineApp.HasExited){throw 'The isolated app exited unexpectedly.'}
  try{if(&$Condition){return}}catch [System.Windows.Automation.ElementNotAvailableException]{}
  catch [System.Management.Automation.MethodInvocationException] {
   # A drop may be committing its short exclusive file transaction when a
   # persistence assertion starts. Retry that read until the transaction ends.
   if($_.Exception.InnerException -isnot [IO.IOException]){throw}
  }
  Start-Sleep -Milliseconds 60
 }while($clock.ElapsedMilliseconds-lt$Timeout)
 $foregroundId=[ShikeInlineInput]::ForegroundProcessId();$foregroundName=(Get-Process -Id $foregroundId -ErrorAction SilentlyContinue).ProcessName
 if([ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){try{Save-InlineFailure $Message}catch{Write-Warning ('Timeout diagnostics: '+$_.Exception.Message)}}
 throw ($Message+' Foreground: '+$foregroundName+' PID '+$foregroundId+'; expected isolated PID '+$inlineApp.Id+'.')
}
function Activate-Inline {
 $root=Get-InlineMain;if($null-eq$root){throw 'The isolated app panel is missing.'}
 $script:inlineHandle=[IntPtr]$root.Current.NativeWindowHandle
 if([ShikeInlineInput]::Activate($inlineHandle)){return}
 Start-Sleep -Milliseconds 150
 if([ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){return}
 [ShikeInlineInput]::SetWindowPos($inlineHandle,[IntPtr]::Zero,0,0,0,0,0x53)|Out-Null
 $rect=$root.Current.BoundingRectangle
 $point=[ShikeInlineInput+Point]::new();$point.X=[int]($rect.X+$rect.Width*0.45);$point.Y=[int]($rect.Y+50)
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw 'Isolated header is covered; activation click skipped.'}
 [ShikeInlineInput]::SetCursorPos($point.X,$point.Y)|Out-Null
 try{[ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 30}finally{[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 600
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Could not activate the isolated app; no keyboard input sent.'}
}
function Reopen-InlineResident {
 $second=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 if(!$second.WaitForExit(3000)){Stop-Process -Id $second.Id;throw 'A second resident process stayed open.'}
 Wait-Inline {[ShikeInlineInput]::IsWindowVisible($inlineHandle)} 'Relaunch did not restore the same panel.'
 Activate-Inline
}
function Close-ReopenInline {
 Invoke-Inline 'Close panel'
 Wait-Inline {![ShikeInlineInput]::IsWindowVisible($inlineHandle)} 'Close did not hide the resident panel.'
 Reopen-InlineResident
}
function Quit-InlineResident {
 if($null-eq$inlineApp-or$inlineApp.HasExited){return}
 $quitRequest=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"'),'--quit') -WindowStyle Hidden -PassThru
 if(!$quitRequest.WaitForExit(3000)){Stop-Process -Id $quitRequest.Id;throw 'The isolated quit request did not finish.'}
 if(!$inlineApp.WaitForExit(3000)){throw 'The isolated app did not quit after the request.'}
}
function Restart-InlineResident {
 Quit-InlineResident
 $script:inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$opened=Get-InlineMain;$null-ne$opened-and[ShikeInlineInput]::IsWindowVisible([IntPtr]$opened.Current.NativeWindowHandle)} 'The isolated app did not reopen after quitting.'
 Activate-Inline
}
function Invoke-Inline([string]$Name,[switch]$Anywhere) {
 if($Name-eq'Quit'){Quit-InlineResident;return}
 if(!$Anywhere){Activate-Inline}
 $control=Find-InlineControl $Name ([System.Windows.Automation.ControlType]::Button) -Anywhere:$Anywhere
 if($null-eq$control){$control=Find-InlineControl $Name -Anywhere:$Anywhere}
 if($null-eq$control){
  $controlClock=[Diagnostics.Stopwatch]::StartNew()
  do{Start-Sleep -Milliseconds 60;$control=Find-InlineControl $Name -Anywhere:$Anywhere}while($null-eq$control-and$controlClock.ElapsedMilliseconds-lt1500)
 }
 if($null-eq$control){
  Save-InlineFailure ('Missing control: '+$Name)
  throw ('Missing control: '+$Name)
 }
 $previousFocusId=''
 try{$focusedBefore=[System.Windows.Automation.AutomationElement]::FocusedElement;if($null-ne$focusedBefore){$previousFocusId=$focusedBefore.GetRuntimeId()-join','}}catch [System.Windows.Automation.ElementNotAvailableException]{}
 $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 # Invoke returns before WPF's queued editor focus; never send Enter to the old focus target.
 if($Name-eq'New topic'-or$Name.StartsWith('Rename ')){Wait-InlineFocus 'Topic name' $previousFocusId}
 elseif($Name-eq'Edit link'-or$Name.StartsWith('Edit link ')){Wait-InlineFocus 'Item link' $previousFocusId}
 elseif($Name.StartsWith('Edit ')-or$Name.StartsWith('Add to ')){Wait-InlineFocus 'Item text' $previousFocusId}
}
function Send-InlineKey([ushort]$Key,[ushort]$Modifier=0) {
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Focus left the isolated app; no keyboard input sent.'}
 try {
  if($Modifier){[ShikeInlineInput]::Key($Modifier,$false)}
  [ShikeInlineInput]::Key($Key,$false);[ShikeInlineInput]::Key($Key,$true)
 } finally {if($Modifier){[ShikeInlineInput]::Key($Modifier,$true)}}
 Start-Sleep -Milliseconds 100
}
function Click-Inline([string]$Name,[System.Windows.Automation.ControlType]$Type=[System.Windows.Automation.ControlType]::Button) {
 Activate-Inline
 $control=Find-InlineControl $Name $Type
 if($null-eq$control-or$control.Current.IsOffscreen){throw ('The mouse target is unavailable: '+$Name)}
 $rect=$control.Current.BoundingRectangle
 if($rect.Width-le 0-or$rect.Height-le 0){throw ('The mouse target has no bounds: '+$Name)}
 $point=[ShikeInlineInput+Point]::new();$point.X=[int]($rect.X+$rect.Width/2);$point.Y=[int]($rect.Y+$rect.Height/2)
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw ('Another window covers the mouse target: '+$Name)}
 [ShikeInlineInput]::SetCursorPos($point.X,$point.Y)|Out-Null
 try {
  [ShikeInlineInput]::MouseButton($false)
  # Exercise the delayed focus-loss handler while the physical button is held.
  Start-Sleep -Milliseconds 70
 } finally {[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 120
}
function Get-InlineInput([string]$Name='Item text') { Find-InlineControl $Name ([System.Windows.Automation.ControlType]::Edit) }
function Scroll-InlineColumn([string]$Group) {
 Activate-Inline
 $control=Find-InlineControl ('Items in '+$Group)
 if($null-eq$control-or$control.Current.IsOffscreen){throw ('The scroll target is unavailable: '+$Group)}
 $rect=$control.Current.BoundingRectangle
 $point=[ShikeInlineInput+Point]::new();$point.X=[int]($rect.X+$rect.Width/2);$point.Y=[int]($rect.Y+$rect.Height/2)
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw ('Another window covers the scroll target: '+$Group)}
 [ShikeInlineInput]::SetCursorPos($point.X,$point.Y)|Out-Null
 [ShikeInlineInput]::MouseWheel(-360)
 Start-Sleep -Milliseconds 200
}
function Column-ScrollPercent([string]$Group) {
 (Find-InlineControl ('Items in '+$Group)).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).Current.VerticalScrollPercent
}
function Input-Value([string]$Name='Item text') {
 $input=Get-InlineInput $Name;if($null-eq$input){return $null}
 $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}
function Input-SelectedText {
 $ranges=(Get-InlineInput).GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern).GetSelection()
 (($ranges|ForEach-Object{$_.GetText(-1)})-join'')
}
function Wait-InlineFocus([string]$Name,[string]$DifferentFrom='') {
 Wait-Inline { $focused=[System.Windows.Automation.AutomationElement]::FocusedElement; $null-ne$focused-and$focused.Current.ProcessId-eq$inlineApp.Id-and$focused.Current.ControlType-eq[System.Windows.Automation.ControlType]::Edit-and$focused.Current.Name-eq$Name-and($DifferentFrom-eq''-or($focused.GetRuntimeId()-join',')-cne$DifferentFrom) } ('Input did not receive keyboard focus: '+$Name)
}
function Set-InlineText([string]$Text,[string]$Name='Item text') {
 Wait-InlineFocus $Name
 $input=Get-InlineInput $Name
 $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
}
function Clear-InlineText {
 Wait-InlineFocus 'Item text'
 Send-InlineKey 0x41 0x11
 Send-InlineKey 0x08
 Wait-Inline {(Input-Value)-ceq''} 'Ctrl+A and Backspace did not clear the item text.'
}
function Read-InlineNote { [IO.File]::ReadAllText($inlineNote,[Text.Encoding]::UTF8) }
function Group-Text([string]$Name) {
 $match=[regex]::Match((Read-InlineNote),'(?ms)^## '+[regex]::Escape($Name)+'\r?\n(.*?)(?=^## |\z)')
 if($match.Success){$match.Groups[1].Value}else{''}
}
function Group-Titles([string]$Name) {
 @([regex]::Matches((Group-Text $Name),'(?m)^- \[[ xX]\](?: ([^\r\n]*))?\r?$')|ForEach-Object{$_.Groups[1].Value})
}
function Empty-TopicKeys {
 @([regex]::Matches((Read-InlineNote),'(?m)^## (<!-- shike-topic:[^\r\n]+ -->)\r?$')|ForEach-Object{$_.Groups[1].Value})
}
function Assert-RowOrder([string]$Name,[string[]]$Expected,[string]$Message) {
 $actual=@(Group-Titles $Name)
 Assert-Inline ($actual.Count-eq$Expected.Count-and($actual-join'|')-ceq($Expected-join'|')) ($Message+' (rows='+$actual.Count+')')
}
function Restore-InlineFixture([string]$Text,[string]$RemovedControl,[string]$PresentControl='Edit Reference') {
 if($null-ne(Get-InlineInput)-or$null-ne(Get-InlineInput 'Topic name')){Send-InlineKey 0x1B}
 [IO.File]::WriteAllText($inlineNote,$Text,$utf8)
 Wait-Inline {$null-eq(Find-InlineControl $RemovedControl)-and$null-ne(Find-InlineControl $PresentControl)} 'The isolated fixture did not refresh after restoration.'
}
function Assert-NoEditorWindows {
 $titles=@([ShikeInlineInput]::VisibleTitles($inlineApp.Id))
 $extra=@($titles|Where-Object{$_-notin@('To-do','To-do glass')})
 Assert-Inline ($titles-contains'To-do'-and$extra.Count-eq 0-and$titles.Count-le 2) ('Editing stays in the panel with no modal editor ('+($titles-join', ')+')')
}
function Assert-CanonicalBlankTasks { Assert-Inline ((Read-InlineNote)-notmatch'(?m)^- \[[ xX]\][ \t]+\r?$') 'Blank rows use the canonical Markdown checkbox with no placeholder or trailing spaces' }
function Assert-NoVisiblePlaceholders([string]$State) {
 $forbidden=@('Name','Paste a link','Add item','Topic name')
 $texts=(Get-InlineMain).FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text))
 $visible=@($texts|Where-Object{!$_.Current.IsOffscreen-and$_.Current.BoundingRectangle.Width-gt0-and$_.Current.BoundingRectangle.Height-gt0-and$_.Current.Name.Trim()-in$forbidden}|ForEach-Object{$_.Current.Name})
 Assert-Inline ($visible.Count-eq0) ($State+' contains no visible placeholder text'+$(if($visible.Count){': '+($visible-join', ')}else{''}))
}
function Stop-InlineHarness {
 [ShikeInlineInput]::ReleaseInputs()
 if($null-ne$inlineApp){
  $inlineApp.Refresh()
  if(!$inlineApp.HasExited){
   try {
    if([ShikeInlineInput]::IsWindowVisible($inlineHandle)-and($null-ne(Get-InlineInput)-or$null-ne(Get-InlineInput 'Topic name'))){
     Activate-Inline
     Send-InlineKey 0x1B
    }
    Quit-InlineResident
   } catch {Write-Warning ('Graceful test cleanup: '+$_.Exception.Message)}
   $inlineApp.Refresh();if(!$inlineApp.HasExited){Stop-Process -Id $inlineApp.Id;$inlineApp.WaitForExit(2000)|Out-Null}
  }
 }
 [ShikeInlineInput]::ReleaseInputs()
 [ShikeInlineInput]::SetCursorPos($originalCursor.X,$originalCursor.Y)|Out-Null
 if($originalForeground-ne[IntPtr]::Zero-and[ShikeInlineInput]::IsWindow($originalForeground)){[ShikeInlineInput]::Activate($originalForeground)|Out-Null}
}
if($HarnessOnly){return}

try {
 $inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$initial=Get-InlineMain;$null-ne$initial-and[ShikeInlineInput]::IsWindowVisible([IntPtr]$initial.Current.NativeWindowHandle)} 'The isolated app did not open.'
 Activate-Inline;Assert-NoEditorWindows;Assert-NoVisiblePlaceholders 'Initial empty cards'
 if(!$ConflictOnly){

 Invoke-Inline 'New topic';Wait-InlineFocus 'Topic name'
 $firstEmpty=@(Empty-TopicKeys)
 Assert-Inline ($firstEmpty.Count-eq1-and(Input-Value 'Topic name')-ceq'') 'New topic saves an unnamed topic immediately before typing'
 Assert-NoVisiblePlaceholders 'Unnamed topic editor'
 $firstKey=$firstEmpty[0]
 Set-InlineText 'Cancelled name' 'Topic name';Send-InlineKey 0x1B
 Wait-Inline {$null-eq(Get-InlineInput 'Topic name')} 'Escape did not leave the topic editor.'
 Assert-Inline ((Empty-TopicKeys)-contains$firstKey-and(Read-InlineNote)-notmatch'Cancelled name'-and$null-ne(Find-InlineControl 'Rename empty topic 2')) 'Escape cancels name edits while retaining the created unnamed topic'
 Invoke-Inline 'New topic';Wait-InlineFocus 'Topic name'
 $emptyKeys=@(Empty-TopicKeys);$secondKey=@($emptyKeys|Where-Object{$_-cne$firstKey})[0]
 Assert-Inline ($emptyKeys.Count-eq2-and$firstKey-cne$secondKey) 'Multiple unnamed topics persist with distinct identities'
 Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Assert-RowOrder $secondKey @('') 'Enter on an unnamed topic saves and focuses its first blank item'
 Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Send-InlineKey 0x0D;Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Assert-RowOrder $secondKey @('','','') 'Repeated empty Enter creates durable blank rows in an unnamed topic'
 Assert-NoVisiblePlaceholders 'Saved unnamed topic and blank rows'
 Invoke-Inline 'Edit empty item 3 in empty topic 3';Set-InlineText 'Later last'
 Invoke-Inline 'Edit empty item 2 in empty topic 3';Set-InlineText 'Later middle'
 Invoke-Inline 'Rename empty topic 2';Wait-InlineFocus 'Topic name';Send-InlineKey 0x1B
 Assert-RowOrder $secondKey @('','Later middle','Later last') 'The last and middle rows can be filled while the first row stays blank'
 Invoke-Inline 'Rename empty topic 2';Set-InlineText 'Later topic' 'Topic name'
 Invoke-Inline 'Edit Later middle';Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Assert-Inline ((Read-InlineNote)-match'(?m)^## Later topic\r?$'-and(Empty-TopicKeys)-contains$secondKey-and$null-ne(Find-InlineControl 'Add to Later topic')) 'An unnamed topic can be named later without changing its sibling identity'
 $beforeRestart=Read-InlineNote
 Close-ReopenInline
 Assert-Inline ((Read-InlineNote)-ceq$beforeRestart-and$null-ne(Find-InlineControl 'Rename empty topic 3')) 'Closing and reopening retains named and unnamed topics'
 Restart-InlineResident
 Assert-Inline ((Read-InlineNote)-ceq$beforeRestart-and$null-ne(Find-InlineControl 'Edit empty item 1 in empty topic 3')) 'A full process restart preserves blank rows and unnamed topics'
 Assert-RowOrder $secondKey @('','Later middle','Later last') 'Out-of-order edits retain their exact positions after restarting'
 Invoke-Inline 'New topic';Wait-InlineFocus 'Topic name';Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-eq(Get-InlineInput 'Topic name')} 'Undo did not leave the freshly created empty topic.'
 Assert-Inline ((Read-InlineNote)-ceq$beforeRestart) 'Ctrl+Z in a freshly created empty topic undoes only that creation'
 Restore-InlineFixture $fixture 'Add to Later topic'
 Assert-Inline ((Read-InlineNote)-ceq$fixture) 'Unnamed-topic regressions restore the original fixture'

 $blankFixture=$fixture.Replace('## Watching list',('## Plans'+[char]10+[char]10+'## Watching list'))
 [IO.File]::WriteAllText($inlineNote,$blankFixture,$utf8)
 Wait-Inline {$null-ne(Find-InlineControl 'Add to Plans')} 'The empty Plans topic did not load.'
 foreach($blankGroup in @('Watching list','Plans')) {
  $beforeBlankFixture=Read-InlineNote
  $prefix=if($blankGroup-eq'Watching list'){'Read'}else{'Plan'}
  $first=$prefix+' first';$middle=$prefix+' middle';$last=$prefix+' last'
  Invoke-Inline ('Add to '+$blankGroup);Wait-InlineFocus 'Item text'
  Assert-RowOrder $blankGroup @('') ($blankGroup+': Add immediately persists one blank item')
  Assert-NoVisiblePlaceholders ($blankGroup+': blank inline editor')
  Send-InlineKey 0x5A 0x11
  Wait-Inline {$null-eq(Get-InlineInput)} 'Undo did not leave the freshly added blank row.'
  Assert-Inline ((Read-InlineNote)-ceq$beforeBlankFixture) ($blankGroup+': Ctrl+Z in a fresh blank row undoes its creation')
  Invoke-Inline ('Add to '+$blankGroup);Wait-InlineFocus 'Item text'
  Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
  Assert-RowOrder $blankGroup @('','') ($blankGroup+': Empty Enter creates exactly one following blank row')
  $beforeAnotherEnter=Read-InlineNote
  Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
  Assert-RowOrder $blankGroup @('','','') ($blankGroup+': Another empty Enter creates a third row and retains focus')
  Send-InlineKey 0x5A 0x11
  Wait-Inline {$null-eq(Get-InlineInput)} 'Undo did not finish the empty continuation.'
  Assert-Inline ((Read-InlineNote)-ceq$beforeAnotherEnter) ($blankGroup+': One Undo reverses exactly one empty Enter')
  Invoke-Inline ('Edit empty item 2 in '+$blankGroup);Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
  Assert-RowOrder $blankGroup @('','','') ($blankGroup+': Enter from the restored second row persists a new third row')
  Set-InlineText $last
  Invoke-Inline ('Edit empty item 2 in '+$blankGroup);Set-InlineText $middle
  Invoke-Inline ('Edit empty item 1 in '+$blankGroup);Send-InlineKey 0x1B
  Assert-RowOrder $blankGroup @('',$middle,$last) ($blankGroup+': Lower rows can be filled before the first row')
  Invoke-Inline ('Edit '+$middle);Send-InlineKey 0x0D;Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
  Assert-RowOrder $blankGroup @('',$middle,'',$last) ($blankGroup+': Enter inside the list leaves blanks at the beginning and middle')
  Close-ReopenInline
  Assert-RowOrder $blankGroup @('',$middle,'',$last) ($blankGroup+': Blank positions survive closing and reopening')
  Assert-NoVisiblePlaceholders ($blankGroup+': saved blank rows after reopening')
  Invoke-Inline ('Edit empty item 1 in '+$blankGroup);Set-InlineText $first
  Invoke-Inline ('Edit '+$last);Send-InlineKey 0x1B
  Assert-RowOrder $blankGroup @($first,$middle,'',$last) ($blankGroup+': Filling the first row later leaves the middle gap in place')

  $beforeRemove=Read-InlineNote
  $blankCheck=Find-InlineControl ('Remove empty item 3 in '+$blankGroup) ([System.Windows.Automation.ControlType]::CheckBox)
  if($null-eq$blankCheck){throw 'The persisted blank row has no remove checkbox.'}
  $blankCheck.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
  Assert-RowOrder $blankGroup @($first,$middle,$last) ($blankGroup+': A blank row can be removed using its circular checkbox')
  Send-InlineKey 0x5A 0x11
  Wait-Inline {$null-ne(Find-InlineControl ('Edit empty item 3 in '+$blankGroup))} 'Undo did not restore the removed blank.'
  Assert-Inline ((Read-InlineNote)-ceq$beforeRemove) ($blankGroup+': Undo restores a removed blank at its original position')
  Invoke-Inline ('Edit empty item 3 in '+$blankGroup)
  Click-Inline 'Remove current empty item' ([System.Windows.Automation.ControlType]::CheckBox)
  Wait-Inline {$null-eq(Get-InlineInput)} 'Removing the active blank left its editor open.'
  Assert-RowOrder $blankGroup @($first,$middle,$last) ($blankGroup+': The active empty row can also be removed using its checkbox')
  Send-InlineKey 0x5A 0x11
  Wait-Inline {$null-ne(Find-InlineControl ('Edit empty item 3 in '+$blankGroup))} 'Undo did not restore the active blank row.'

  $beforeClear=Read-InlineNote
  Invoke-Inline ('Edit '+$middle);Clear-InlineText;Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
  Assert-RowOrder $blankGroup @($first,'','','',$last) ($blankGroup+': Clearing keeps the current blank and Enter adds a following blank')
  Assert-Inline ((Input-Value)-ceq''-and$null-eq(Find-InlineControl ('Edit '+$middle))) ($blankGroup+': The new continuation has focus and the old text is gone')
  Send-InlineKey 0x5A 0x11
  Wait-Inline {$null-eq(Get-InlineInput)-and$null-ne(Find-InlineControl ('Edit '+$middle))} 'Undo in the continuation did not restore the cleared item.'
  Assert-Inline ((Read-InlineNote)-ceq$beforeClear) ($blankGroup+': One Undo reverses both the edit and the Enter-created row')
  Invoke-Inline ('Edit '+$middle);Clear-InlineText;Send-InlineKey 0x1B
  Assert-Inline ((Read-InlineNote)-ceq$beforeClear) ($blankGroup+': Escape cancels clearing without removing the row')
  Invoke-Inline ('Edit '+$middle);Send-InlineKey 0x41 0x11
  foreach($space in 1..3){Send-InlineKey 0x20}
  Click-Inline ('Edit '+$last);Wait-InlineFocus 'Item text'
  Assert-Inline ((Input-Value)-ceq$last) ($blankGroup+': Clearing and clicking another row preserves its editor focus')
  Assert-RowOrder $blankGroup @($first,'','',$last) ($blankGroup+': Whitespace saves as a blank on blur without changing the row count')
  Send-InlineKey 0x1B;Close-ReopenInline
  Assert-RowOrder $blankGroup @($first,'','',$last) ($blankGroup+': Reopening cannot resurrect text cleared by leaving the editor')
  Assert-CanonicalBlankTasks
  Restore-InlineFixture $beforeBlankFixture ('Edit '+$first)
  Assert-Inline ((Read-InlineNote)-ceq$beforeBlankFixture) ($blankGroup+': Durable-blank regressions restore the original fixture')
 }

 $beforeScrolling=Read-InlineNote
 $scrollFixture=$beforeScrolling.Replace(('## Watching list'+[char]10),('## Watching list'+[char]10+((1..18|ForEach-Object{'- [ ] Scroll watch '+$_})-join[char]10)+[char]10)).Replace(('## Plans'+[char]10),('## Plans'+[char]10+((1..18|ForEach-Object{'- [ ] Scroll plan '+$_})-join[char]10)+[char]10))
 [IO.File]::WriteAllText($inlineNote,$scrollFixture,$utf8)
 Wait-Inline {$null-ne(Find-InlineControl 'Edit Scroll watch 1')-and$null-ne(Find-InlineControl 'Edit Scroll plan 1')} 'The scrolling fixture did not load.'
 $planScrollBefore=Column-ScrollPercent 'Plans'
 Scroll-InlineColumn 'Watching list'
 Wait-Inline {(Column-ScrollPercent 'Watching list')-gt0} 'Mouse wheel did not scroll the watching column.'
 Assert-Inline ((Column-ScrollPercent 'Plans')-eq$planScrollBefore) 'Scrolling Watching list leaves the topic column stationary'
 $watchingScrollBefore=Column-ScrollPercent 'Watching list'
 Scroll-InlineColumn 'Plans'
 Wait-Inline {(Column-ScrollPercent 'Plans')-gt$planScrollBefore} 'Mouse wheel did not scroll the topic column.'
 Assert-Inline ((Column-ScrollPercent 'Watching list')-eq$watchingScrollBefore-and(Read-InlineNote)-ceq$scrollFixture) 'Scrolling a topic leaves Watching list stationary and never edits the note'
 Restore-InlineFixture $beforeScrolling 'Edit Scroll watch 1'

 $beforeWatchingLink=Read-InlineNote
 Invoke-Inline 'Add to Watching list';Wait-InlineFocus 'Item text'
 Assert-Inline ($null-ne(Get-InlineInput 'Item link')-and!(Get-InlineInput 'Item link').Current.IsOffscreen) 'Watching cards expose both name and link fields immediately'
 Assert-NoVisiblePlaceholders 'Blank Watching name and link fields'
 Send-InlineKey 0x09;Wait-InlineFocus 'Item link'
 Set-InlineText 'https://example.net/watch-later' 'Item link';Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Assert-Inline (@(Group-Titles 'Watching list').Count-eq2-and(Group-Text 'Watching list').Contains('[](<https://example.net/watch-later>)')-and(Input-Value)-ceq'') 'Link-first Enter saves the URL and focuses one following blank watching card'
 Invoke-Inline 'Edit https://example.net/watch-later';Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq''-and(Input-Value 'Item link')-ceq'https://example.net/watch-later') 'A link-only watching card reopens with its name still empty'
 Set-InlineText 'Watch later';Send-InlineKey 0x09;Wait-InlineFocus 'Item link'
 Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Assert-Inline (@(Group-Titles 'Watching list').Count-eq3-and(Group-Text 'Watching list').Contains('[Watch later](<https://example.net/watch-later>)')) 'Naming an earlier link-only card preserves its URL and link Enter inserts directly below it'
 Send-InlineKey 0x1B
 $beforeWatchingRestart=Read-InlineNote
 Close-ReopenInline
 Invoke-Inline 'Edit Watch later';Wait-InlineFocus 'Item text'
 Assert-Inline ((Read-InlineNote)-ceq$beforeWatchingRestart-and(Input-Value 'Item link')-ceq'https://example.net/watch-later') 'Watching card name, URL and blank positions survive closing and reopening'
 Clear-InlineText
 Invoke-Inline 'Rename Existing';Wait-InlineFocus 'Topic name';Send-InlineKey 0x1B
 Assert-Inline ((Group-Text 'Watching list').Contains('[](<https://example.net/watch-later>)')-and@(Group-Titles 'Watching list').Count-eq3) 'Clearing only a watching name leaves its URL and row position intact'
 Restore-InlineFixture $beforeWatchingLink 'Edit https://example.net/watch-later'
 Assert-Inline ((Read-InlineNote)-ceq$beforeWatchingLink) 'Watching link regressions restore the original fixture'

 $namedFixture=$blankFixture.Replace(('## Plans'+[char]10),('## Plans'+[char]10+'- [ ] First item'+[char]10+'- [ ] Second item'+[char]10))
 [IO.File]::WriteAllText($inlineNote,$namedFixture,$utf8)
 Wait-Inline {$null-ne(Find-InlineControl 'Edit First item')} 'The named-item fixture did not load.'
 Invoke-Inline 'Rename Existing';Set-InlineText 'Plans' 'Topic name';Send-InlineKey 0x0D
 Assert-Inline ((Input-Value 'Topic name')-ceq'Plans'-and(Read-InlineNote)-ceq$namedFixture) 'Duplicate topic validation retains the proposed name and all saved rows'
 Send-InlineKey 0x1B
 Invoke-Inline 'Edit First item';Set-InlineText 'First revised';Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Set-InlineText 'Inserted between';Send-InlineKey 0x0D;Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Assert-RowOrder 'Plans' @('First revised','Inserted between','','Second item') 'Successive Enter saves text and persists a blank exactly below the edited row'

 Invoke-Inline 'Edit Reference';Set-InlineText 'Reference revised'
 Click-Inline 'Edit Following';Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq'Following'-and(Input-SelectedText)-ceq'') 'A first mouse click edits the row directly without selecting all its text'
 Send-InlineKey 0x1B
 Assert-Inline ((Group-Text 'Existing').Contains('[Reference revised](<https://example.com/keep?x=1&y=2>)')-and(Group-Text 'Existing').Contains('  > Keep this note.')) 'Editing a title preserves its link and note'
 Invoke-Inline 'Rename Existing';Set-InlineText 'Renamed' 'Topic name'
 Click-Inline 'Edit Following';Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Assert-Inline ((Read-InlineNote)-notmatch'(?m)^## Existing\r?$'-and(Group-Text 'Renamed').Contains('https://example.com/keep?x=1&y=2')-and(Group-Text 'Renamed').Contains('  > Keep this note.')) 'Renaming a topic on blur preserves items and metadata'
 $beforeMouseRegression=Read-InlineNote
 Invoke-Inline 'Rename Renamed';Set-InlineText 'Renamed by click' 'Topic name'
 Click-Inline 'Complete Following' ([System.Windows.Automation.ControlType]::CheckBox)
 Wait-Inline {$null-eq(Find-InlineControl 'Complete Following')} 'The checkbox click was lost after the pending topic rename.'
 $renamedText=Read-InlineNote
 Assert-Inline ($renamedText-notmatch'(?m)^## Renamed\r?$'-and[regex]::Matches($renamedText,'(?m)^## Renamed by click\r?$').Count-eq1-and(Group-Text 'Renamed by click')-match'(?m)^- \[x\] Following\r?$') 'A real checkbox click commits its pending rename without recreating the old heading'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-ne(Find-InlineControl 'Complete Following')} 'Undo did not restore the item completed during renaming.'
 Assert-Inline ((Read-InlineNote)-match'(?m)^## Renamed by click\r?$'-and(Read-InlineNote)-notmatch'(?m)^## Renamed\r?$') 'Undo reverses completion while retaining the committed topic name'
 Invoke-Inline 'Rename Renamed by click';Set-InlineText 'Renamed' 'Topic name'
 Click-Inline 'Edit Following';Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Assert-Inline ((Read-InlineNote)-ceq$beforeMouseRegression) 'Restoring the topic name leaves the fixture unchanged'
 Invoke-Inline 'Edit Reference revised';Set-InlineText 'Reference committed by click'
 Click-Inline 'Edit Following';Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq'Following'-and(Group-Text 'Renamed').Contains('[Reference committed by click](<https://example.com/keep?x=1&y=2>)')-and(Group-Text 'Renamed').Contains('  > Keep this note.')) 'A real click commits the first edit and focuses the clicked row'
 Send-InlineKey 0x1B
 Invoke-Inline 'Edit Reference committed by click';Set-InlineText 'Reference revised'
 Click-Inline 'Edit Following';Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Assert-Inline ((Read-InlineNote)-ceq$beforeMouseRegression) 'Switching editors by mouse preserves both rows and metadata'

 $beforeMetadataClear=Read-InlineNote
 Invoke-Inline 'Edit Reference revised';Clear-InlineText;Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Assert-Inline (@(Group-Titles 'Renamed').Count-eq3-and!(Group-Text 'Renamed').Contains('Reference revised')) 'Clearing a linked title retains its row and creates one following blank'
 Assert-Inline ((Group-Text 'Renamed').Contains('https://example.com/keep?x=1&y=2')-and(Group-Text 'Renamed').Contains('Keep this note.')) 'Clearing only the title preserves the independent link and note'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-ne(Find-InlineControl 'Edit Reference revised')} 'Undo did not restore the cleared linked row.'
 Assert-Inline ((Read-InlineNote)-ceq$beforeMetadataClear) 'One Undo restores cleared text, link and note and removes the added blank'
 Activate-Inline
 $reference=Find-InlineControl 'Edit Reference revised' ([System.Windows.Automation.ControlType]::Button)
 $reference.SetFocus();Send-InlineKey 0x79 0x10
 Wait-Inline {$null-ne(Find-InlineControl 'Edit link' -Anywhere)} 'The context menu did not open.'
 Invoke-Inline 'Edit link' -Anywhere;Wait-InlineFocus 'Item link'
 Send-InlineKey 0x09 0x10;Wait-InlineFocus 'Item text'
 Send-InlineKey 0x09;Wait-InlineFocus 'Item link'
 Set-InlineText 'https://example.org/updated' 'Item link';Send-InlineKey 0x0D
 Wait-Inline {$null-eq(Get-InlineInput 'Item link')} 'Link Enter did not commit.'
 Assert-Inline ((Group-Text 'Renamed').Contains('[Reference revised](<https://example.org/updated>)')-and(Group-Text 'Renamed').Contains('  > Keep this note.')-and@(Group-Titles 'Renamed').Count-eq2) 'Tab moves within link editing; link Enter updates metadata without adding a row'
 Assert-NoEditorWindows
 $before=Read-InlineNote
 $check=Find-InlineControl 'Complete Second item' ([System.Windows.Automation.ControlType]::CheckBox)
 $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
 Wait-Inline {$null-eq(Find-InlineControl 'Complete Second item')} 'A completed item stayed visible.'
 Assert-Inline ((Read-InlineNote)-match'(?m)^- \[x\] Second item\r?$') 'Checking a named item removes it from view and saves completion'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-ne(Find-InlineControl 'Complete Second item')} 'Undo did not restore the completed item.'
 Assert-Inline ((Read-InlineNote)-ceq$before) 'Keyboard Undo restores the previous durable list including blank positions'
 Invoke-Inline 'Add to Plans';Set-InlineText 'Saved on close';Close-ReopenInline
 Assert-Inline ((Group-Text 'Plans').Contains('- [ ] Saved on close')-and$null-ne(Find-InlineControl 'Edit Saved on close')) 'Closing commits active text and reopening restores the saved row'
 }

 $beforeNoopRegression=Read-InlineNote
 Invoke-Inline 'Edit First revised';Wait-InlineFocus 'Item text'
 $externalModified=[regex]::Replace($beforeNoopRegression,'(?m)^- \[ \] First revised\r?$','- [ ] First externally revised')
 [IO.File]::WriteAllText($inlineNote,$externalModified,$utf8);Send-InlineKey 0x0D
 Wait-Inline {$null-eq(Get-InlineInput)-and$null-ne(Find-InlineControl 'Edit First externally revised')} 'Unchanged Enter retained an externally modified item as a stale editor.'
 Assert-Inline ((Read-InlineNote)-ceq$externalModified-and$null-eq(Find-InlineControl 'Edit First revised')) 'An unchanged stale editor accepts external modification without a phantom row'
 Restore-InlineFixture $beforeNoopRegression 'Edit First externally revised' 'Edit First revised'
 Invoke-Inline 'Edit Inserted between';Wait-InlineFocus 'Item text'
 $externalDeleted=[regex]::Replace($beforeNoopRegression,'(?m)^- \[ \] Inserted between\r?\n','')
 [IO.File]::WriteAllText($inlineNote,$externalDeleted,$utf8);Send-InlineKey 0x0D
 Wait-Inline {$null-eq(Get-InlineInput)-and$null-eq(Find-InlineControl 'Edit Inserted between')} 'Unchanged Enter retained an externally deleted item as a stale editor.'
 Assert-Inline ((Read-InlineNote)-ceq$externalDeleted) 'An unchanged editor accepts external deletion without restoring or advancing from it'
 [IO.File]::WriteAllText($inlineNote,$beforeNoopRegression,$utf8)
 Wait-Inline {$null-ne(Find-InlineControl 'Edit Inserted between')} 'The deleted-item fixture did not refresh.'
 Invoke-Inline 'Rename Plans';Wait-InlineFocus 'Topic name'
 $externalRenamed=[regex]::Replace($beforeNoopRegression,'(?m)^## Plans\r?$','## Plans externally renamed')
 [IO.File]::WriteAllText($inlineNote,$externalRenamed,$utf8);Send-InlineKey 0x0D
 Wait-Inline {$null-eq(Get-InlineInput 'Topic name')-and$null-eq(Get-InlineInput)-and$null-ne(Find-InlineControl 'Add to Plans externally renamed')} 'Unchanged topic Enter left an invisible editor after external renaming.'
 Assert-Inline ((Read-InlineNote)-ceq$externalRenamed-and$null-eq(Find-InlineControl 'Add to Plans')) 'An unchanged topic accepts external renaming without an obsolete heading or blank insertion'
 Restore-InlineFixture $beforeNoopRegression 'Add to Plans externally renamed' 'Add to Plans'
 Invoke-Inline 'Add to Plans';Set-InlineText 'Keep this unsaved draft'
 Start-Sleep -Milliseconds 1250
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){
  $foregroundId=[ShikeInlineInput]::ForegroundProcessId();$foregroundName=(Get-Process -Id $foregroundId -ErrorAction SilentlyContinue).ProcessName
  throw ('Foreground changed during the editor hold to '+$foregroundName+' (PID '+$foregroundId+'); rerun without competing desktop input.')
 }
 Assert-Inline ((Input-Value)-ceq'Keep this unsaved draft') 'The file watcher does not rebuild an active editor'
 $external=[regex]::Replace((Read-InlineNote),'(?ms)^## Plans\r?\n.*?(?=^## |\z)','')
 [IO.File]::WriteAllText($inlineNote,$external,$utf8)
 Start-Sleep -Milliseconds 1250;Wait-InlineFocus 'Item text';Send-InlineKey 0x0D
 $errorLabel=Find-InlineControl 'Input error' ([System.Windows.Automation.ControlType]::Text)
 Assert-Inline ((Input-Value)-ceq'Keep this unsaved draft'-and$null-ne$errorLabel-and!$errorLabel.Current.IsOffscreen) 'External topic deletion keeps unsaved text with an inline conflict error'
 Assert-Inline ((Read-InlineNote)-ceq$external) 'A conflicting draft cannot recreate the deleted topic or insert a blank into it'
 Send-InlineKey 0x1B
 Wait-Inline {$null-eq(Get-InlineInput)-and$null-eq(Find-InlineControl 'Add to Plans')} 'Escape did not refresh after external deletion.'
 Assert-Inline ((Group-Text 'Renamed').Contains('https://example.org/updated')-and(Group-Text 'Renamed').Contains('Keep this note.')) 'Conflict recovery preserves unaffected topics and metadata'
 Assert-CanonicalBlankTasks;Assert-NoEditorWindows
 ('PASS '+$passCount+' inline UI assertions; isolated data: '+$inlineVault)
} finally {
 Stop-InlineHarness
}
