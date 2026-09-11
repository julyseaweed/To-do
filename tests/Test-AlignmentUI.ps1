param([string]$ApplicationPath,[switch]$ValidateOnly,[switch]$VisibleFixture,[switch]$RemainingOnly)
$ErrorActionPreference='Stop'
if($ValidateOnly){& (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ValidateOnly;return}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShikeAlignmentDpi {
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
'@
[ShikeAlignmentDpi]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
. (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ApplicationPath $ApplicationPath -HarnessOnly
$taskTitle='Alpha middle end'
$watchTitle='Before sunrise we collect several thoughtful ideas and then find the middle marker before the long story concludes.'
$watchLink='https://example.net/watch/segment-one/segment-two/segment-three/middle-marker/segment-four/segment-five?source=alignment-check'
$alignmentFixture="# Current`n`n## Plans`n- [ ] $taskTitle`n- [ ] Following task`n`n## Other`n- [ ] Other unchanged`n`n## Watching list`n- [ ] [$watchTitle](<$watchLink>)`n- [ ] Watching following`n"
[IO.File]::WriteAllText($inlineNote,$alignmentFixture,$utf8)
if($VisibleFixture){
 # Only this temporary vault's panel stays above other windows so the
 # guarded activation click remains available under foreground restrictions.
 $fixtureSettingsPath=Join-Path $inlineState 'settings.json'
 $fixtureSettings=Get-Content -LiteralPath $fixtureSettingsPath -Raw|ConvertFrom-Json
 $fixtureSettings.Pinned=$true
 [IO.File]::WriteAllText($fixtureSettingsPath,($fixtureSettings|ConvertTo-Json),$utf8)
}

function Settings-Alignment { Get-Content -LiteralPath (Join-Path $inlineState 'settings.json') -Raw | ConvertFrom-Json }
function Rect-Alignment([string]$Name) {
 $control=Find-InlineControl $Name
 if($null-eq$control-or$control.Current.BoundingRectangle.Width-le0-or$control.Current.BoundingRectangle.Height-le0){throw ('Control bounds unavailable: '+$Name)}
 $control.Current.BoundingRectangle
}
function Click-AlignmentPoint([double]$X,[double]$Y) {
 Activate-Inline
 $point=[ShikeInlineInput+Point]::new();$point.X=[int][Math]::Round($X);$point.Y=[int][Math]::Round($Y)
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw 'The calibrated text point is covered; click cancelled.'}
 [ShikeInlineInput]::SetCursorPos($point.X,$point.Y)|Out-Null
 try{[ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 60}finally{[ShikeInlineInput]::MouseButton($true)}
}
function Test-PointerCaret([string]$Action,[string]$Field,[string]$Text,[string]$Token,[string]$Group,[bool]$Wrapped) {
 $beforeNote=Read-InlineNote
 $savedBounds=Rect-Alignment $Action
 $beforeScroll=Column-ScrollPercent $Group
 # UIA glyph rectangles are virtualized inconsistently on this display.
 # Use the saved control's physical rectangle and inspect the actual insertion.
 $pointX=$savedBounds.Left+$savedBounds.Width*.32
 $pointY=$savedBounds.Top+$savedBounds.Height*$(if($Wrapped){.45}else{.5})
 if($Wrapped){Assert-Inline ($savedBounds.Height-gt50) ($Field+' saved text is visibly wrapped before the pointer test')}
 Click-AlignmentPoint $pointX $pointY;Wait-InlineFocus $Field
 $activeBounds=(Get-InlineInput $Field).Current.BoundingRectangle
 Assert-Inline ([Math]::Abs($savedBounds.Left-$activeBounds.Left)-lt2-and[Math]::Abs($savedBounds.Top-$activeBounds.Top)-lt2-and[Math]::Abs($savedBounds.Width-$activeBounds.Width)-lt2-and[Math]::Abs($savedBounds.Height-$activeBounds.Height)-lt2-and[Math]::Abs((Column-ScrollPercent $Group)-$beforeScroll)-lt.01) ($Field+' pointer activation keeps text bounds and column scrolling stationary')
 if($Wrapped-and!$script:alignmentCaptured){
  $script:alignmentCaptured=$true
  & (Join-Path $PSScriptRoot 'Inspect-Window.ps1') -AppId $inlineApp.Id -OutputPath (Join-Path $inlineVault 'native-wrapped-edit.png') -RequireActive
 }
 Send-InlineKey 0x58
 $typed=Input-Value $Field
 $insertedInside=$false
 if($typed.Length-eq$Text.Length+1){
  for($index=1;$index-lt$Text.Length-1;$index++){
   if($typed-ceq$Text.Insert($index,'x')-or$typed-ceq$Text.Insert($index,'X')){$insertedInside=$true;break}
  }
 }
 Assert-Inline $insertedInside ($Field+' typing inserts within the clicked saved text rather than appending at the end')
 Send-InlineKey 0x1B
 Assert-Inline ((Read-InlineNote)-ceq$beforeNote) ($Field+' cancelled pointer edit leaves saved content unchanged')
}
function Drag-AlignmentDivider([int]$Delta) {
 Activate-Inline
 $rect=Rect-Alignment 'Resize Watching list'
 $point=[ShikeInlineInput+Point]::new();$point.X=[int]($rect.Left+$rect.Width/2);$point.Y=[int]($rect.Top+[Math]::Min(100,$rect.Height/2))
 if([ShikeInlineInput]::GetAncestor([ShikeInlineInput]::WindowFromPoint($point),2)-ne$inlineHandle){throw 'The divider handle is covered; drag cancelled.'}
 [ShikeInlineInput]::SetCursorPos($point.X,$point.Y)|Out-Null
 try{
  [ShikeInlineInput]::MouseButton($false)
  for($step=1;$step-le8;$step++){
   if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Focus left the isolated app during the divider drag.'}
   [ShikeInlineInput]::SetCursorPos(($point.X+[int]($Delta*$step/8)),$point.Y)|Out-Null
   Start-Sleep -Milliseconds 20
  }
 }finally{[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 120
}
try {
 $inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$opened=Get-InlineMain;$null-ne$opened-and[ShikeInlineInput]::IsWindowVisible([IntPtr]$opened.Current.NativeWindowHandle)} 'The isolated alignment app did not open.'
 Activate-Inline;Assert-NoEditorWindows
 $script:alignmentCaptured=$RemainingOnly
 if(!$RemainingOnly){
  Test-PointerCaret ('Edit '+$taskTitle) 'Item text' $taskTitle 'middle' 'Plans' $false
  Test-PointerCaret ('Edit '+$watchTitle) 'Item text' $watchTitle 'middle' 'Watching list' $true
 }
 Test-PointerCaret ('Edit link '+$watchTitle) 'Item link' $watchLink 'middle-marker' 'Watching list' $true

 Click-Inline 'Rename Watching list';Wait-InlineFocus 'Topic name'
 Set-InlineText 'To watch' 'Topic name';Send-InlineKey 0x0D;Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Wait-Inline {(Settings-Alignment).WatchingTitle-ceq'To watch'} 'The custom Watching title did not persist to settings.'
 Close-ReopenInline
 Click-Inline 'Rename Watching list';Wait-InlineFocus 'Topic name'
 Assert-Inline ((Input-Value 'Topic name')-ceq'To watch') 'Watching title remains renamed after closing and reopening the panel'
 Send-InlineKey 0x41 0x11;Send-InlineKey 0x08;Send-InlineKey 0x0D;Wait-InlineFocus 'Item text';Send-InlineKey 0x1B
 Wait-Inline {(Settings-Alignment).WatchingTitle-ceq''} 'An empty Watching title did not persist.'
 Assert-Inline ((Read-InlineNote)-match'(?m)^## Watching list\r?$') 'Renaming and clearing the Watching label preserve its stored list identity'

 $watchBefore=Rect-Alignment 'Items in Watching list';$topicBefore=Rect-Alignment 'Items in Plans'
 Drag-AlignmentDivider 120
 $watchWider=Rect-Alignment 'Items in Watching list';$topicShifted=Rect-Alignment 'Items in Plans'
 Assert-Inline ($watchWider.Width-gt$watchBefore.Width+60-and$topicShifted.Left-gt$topicBefore.Left+60) 'Dragging the divider right widens Watching and moves the topic boundary'
 Drag-AlignmentDivider -90
 $watchNarrower=Rect-Alignment 'Items in Watching list'
 Assert-Inline ($watchNarrower.Width-lt$watchWider.Width-40) 'Dragging the divider left narrows Watching again'
 Wait-Inline {(Settings-Alignment).WatchingWidth-gt0} 'The resized Watching width was not saved.'
 $savedWidth=(Settings-Alignment).WatchingWidth
 Restart-InlineResident
 Assert-Inline ((Settings-Alignment).WatchingTitle-ceq''-and[Math]::Abs((Settings-Alignment).WatchingWidth-$savedWidth)-lt.1) 'The empty Watching title and chosen divider width survive a full restart'
 Click-Inline 'Rename Watching list';Wait-InlineFocus 'Topic name'
 Assert-Inline ((Input-Value 'Topic name')-ceq'') 'Clicking the unnamed Watching header reopens an empty editor without a fabricated title'
 Send-InlineKey 0x1B
 Click-Inline ('Edit '+$taskTitle);Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq$taskTitle) 'A saved task stays directly clickable after divider dragging and restart'
 Send-InlineKey 0x1B
 Click-Inline 'Add to Plans';Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq'') 'The topic add button remains clickable after resizing Watching'
 Send-InlineKey 0x1B;Assert-NoEditorWindows
 'PASS '+$passCount+' alignment, Watching title and divider assertions; isolated data: '+$inlineVault
}finally{Stop-InlineHarness}
