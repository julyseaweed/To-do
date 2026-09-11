param([int]$AppId, [string]$Vault)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
$root = Get-ShikeWindow -AppId $AppId
if ($null -eq $root) { throw 'The requested test app window was not found.' }
if (-not ('ShikeInlineUiKeys' -as [type])) {
 Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShikeInlineUiKeys {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first,uint second,bool attach);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 public static void Activate(IntPtr target) {
  uint ignored,current=GetCurrentThreadId();
  uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out ignored);
  bool attached=foreground!=0&&foreground!=current&&AttachThreadInput(current,foreground,true);
  try {SetForegroundWindow(target);} finally {if(attached)AttachThreadInput(current,foreground,false);}
 }
}
'@
}
[ShikeInlineUiKeys]::SetProcessDPIAware() | Out-Null
function Find-Element($parent, $name) {
 $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
 $element = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
 if ($null -eq $element) { throw "Missing UI element: $name" }
 return $element
}
function Invoke-Element($parent, $name) { (Find-Element $parent $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 200 }
function Find-Input($parent, $name) {
 $condition = [System.Windows.Automation.AndCondition]::new(
  [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name),
  [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))
 $inlineField = $null
 for ($attempt = 0; $attempt -lt 20; $attempt++) {
  $inlineField = $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if ($null -ne $inlineField) { return $inlineField }
  Start-Sleep -Milliseconds 50
 }
 throw "Missing inline input: $name"
}
function Focus-Input($inlineField) {
 $handle = [IntPtr]$root.Current.NativeWindowHandle
 [ShikeInlineUiKeys]::Activate($handle)
 try { $inlineField.SetFocus() } catch [System.InvalidOperationException] { }
 Start-Sleep -Milliseconds 100
 if ([ShikeInlineUiKeys]::GetForegroundWindow() -ne $handle -or !$inlineField.Current.HasKeyboardFocus) {
  $bounds = $inlineField.Current.BoundingRectangle
  if ($inlineField.Current.IsOffscreen -or $bounds.Width -le 0 -or $bounds.Height -le 0) { throw 'The inline input is not visible; keyboard input cancelled.' }
  $point = [ShikeInlineUiKeys+Point]::new(); $point.X = [int]($bounds.X + $bounds.Width/2); $point.Y = [int]($bounds.Y + $bounds.Height/2)
  if ([ShikeInlineUiKeys]::GetAncestor([ShikeInlineUiKeys]::WindowFromPoint($point),2) -ne $handle) { throw 'The inline input is covered; keyboard input cancelled.' }
  $previous = [ShikeInlineUiKeys+Point]::new()
  $restoreCursor = [ShikeInlineUiKeys]::GetCursorPos([ref]$previous)
  try {
   [ShikeInlineUiKeys]::SetCursorPos($point.X,$point.Y) | Out-Null
   [ShikeInlineUiKeys]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  } finally {
   [ShikeInlineUiKeys]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
   if ($restoreCursor) { [ShikeInlineUiKeys]::SetCursorPos($previous.X,$previous.Y) | Out-Null }
  }
  for ($attempt = 0; $attempt -lt 20; $attempt++) {
   if ([ShikeInlineUiKeys]::GetForegroundWindow() -eq $handle -and $inlineField.Current.HasKeyboardFocus) { break }
   Start-Sleep -Milliseconds 25
  }
 }
 if ([ShikeInlineUiKeys]::GetForegroundWindow() -ne $handle -or !$inlineField.Current.HasKeyboardFocus) { throw 'Cannot focus the intended inline input; no keys were sent.' }
}
function Set-Input($parent, $name, $value) {
 $inlineField = Find-Input $parent $name
 Focus-Input $inlineField
 $inlineField.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}
function Send-InputKey($name, $key) {
 $inlineField = Find-Input $root $name
 Focus-Input $inlineField
 [System.Windows.Forms.SendKeys]::SendWait($key)
 Start-Sleep -Milliseconds 200
}
function Assert-UI($condition,$label) { if (!$condition) { throw $label }; Write-Output "PASS $label" }
$notePath = Join-Path $Vault '当前.md'
Assert-UI ((Get-Content -LiteralPath $notePath -Raw) -match '## To read' -and (Get-Content -LiteralPath $notePath -Raw) -notmatch '- \[') 'First launch has an empty reading column and no assumed tasks'
foreach ($topic in @('测试 · 阅读','测试 · 项目','测试 · 学习')) {
 Invoke-Element $root 'New topic'
 Set-Input $root 'Topic name' $topic
 Send-InputKey 'Topic name' '{ENTER}'
 Send-InputKey 'Item text' '{ESC}'
}
$a = (Find-Element $root '测试 · 阅读').Current.BoundingRectangle
$b = (Find-Element $root '测试 · 项目').Current.BoundingRectangle
$c = (Find-Element $root '测试 · 学习').Current.BoundingRectangle
Assert-UI ($a.X -lt $b.X -and $b.X -lt $c.X -and [Math]::Abs($a.Y-$b.Y) -lt 3) 'Topics appear side by side in a wide window'
$article = 'https://example.com/article'
Invoke-Element $root 'Add to 测试 · 阅读'
Set-Input $root 'Item text' $article
Send-InputKey 'Item text' '{ENTER}'
Assert-UI ((Get-Content -LiteralPath $notePath -Raw) -match ('\[' + [Regex]::Escape($article) + '\]\(<' + [Regex]::Escape($article) + '>\)')) 'Entering a URL inline persists it as a Markdown link'
Assert-UI ($null -ne (Find-Element $root ('Open ' + $article))) 'Saved link has an accessible open action'
Set-Input $root 'Item text' 'UI 测试 · 第二条'
Send-InputKey 'Item text' '{ENTER}'
Send-InputKey 'Item text' '{ESC}'
$firstRow = (Find-Element $root ('Edit ' + $article)).Current.BoundingRectangle
$secondRow = (Find-Element $root 'Edit UI 测试 · 第二条').Current.BoundingRectangle
Assert-UI ([Math]::Abs($firstRow.X-$secondRow.X) -lt 3 -and $firstRow.Y -lt $secondRow.Y) 'Items are vertically stacked inside each topic'
$check = Find-Element $root ('Complete ' + $article)
$check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
Start-Sleep -Milliseconds 200
Assert-UI ((Get-Content -LiteralPath $notePath -Raw) -match ('- \[x\] \[' + [Regex]::Escape($article) + '\]')) 'Checkbox persists completion'
$completedCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,('Complete ' + $article))
Assert-UI ($null -eq $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$completedCondition)) 'Completed task immediately disappears from the panel'
Invoke-Element $root 'Undo (Ctrl+Z)'
Assert-UI ((Get-Content -LiteralPath $notePath -Raw) -match ('- \[ \] \[' + [Regex]::Escape($article) + '\]')) 'Undo restores the active task'
Add-Content -LiteralPath $notePath -Value '- [ ] 外部Edit验证'
Start-Sleep -Milliseconds 1400
Assert-UI ($null -ne (Find-Element $root 'Complete 外部Edit验证')) 'External Markdown changes appear without restarting'
if ($root.Current.BoundingRectangle.Height -lt 200) { Invoke-Element $root 'Open list'; Start-Sleep -Milliseconds 100 }
$transform = $root.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
$originalBounds = $root.Current.BoundingRectangle
$transform.Resize(720,900)
$transform.Move(180,180)
Start-Sleep -Milliseconds 200
$small = $root.Current.BoundingRectangle
Assert-UI ([Math]::Abs($small.Width-720) -lt 5 -and [Math]::Abs($small.Height-900) -lt 5) 'Native resize responds with requested dimensions'
Assert-UI ([Math]::Abs($small.X-180) -lt 5 -and [Math]::Abs($small.Y-180) -lt 5) 'Native window moves freely'
Assert-UI (!(Find-Element $root 'New topic').Current.IsOffscreen) 'The add action stays visible in a small window'
Invoke-Element $root 'Collapse / Expand'
Assert-UI ($root.Current.BoundingRectangle.Height -lt 200) 'Collapse reduces the window to a circular icon'
Invoke-Element $root 'Open list'
Assert-UI ($root.Current.BoundingRectangle.Height -gt 700) 'Expand restores the previous height'
$transform.Resize($originalBounds.Width,$originalBounds.Height)
$transform.Move($originalBounds.X,$originalBounds.Y)
Write-Output 'UI checks complete. All changes are confined to the temporary test vault.'
