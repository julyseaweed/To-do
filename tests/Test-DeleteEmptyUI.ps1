param([string]$ApplicationPath, [switch]$ValidateOnly, [switch]$TailOnly)
$ErrorActionPreference='Stop'
if($ValidateOnly){& (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ValidateOnly;return}
# Reuse the isolated vault, verified native input, UIA focus waits and cleanup,
# without executing the full inline editing regression suite.
. (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ApplicationPath $ApplicationPath -HarnessOnly
$deleteBase="# Current`n`n## Plans`n- [ ] Previous`n- [ ] Target`n- [ ] Next`n`n## Other`n- [ ] Other unaffected`n`n## Watching list`n- [ ] [Watch target](<https://example.net/delete-check>)`n- [ ] Watch next`n"
[IO.File]::WriteAllText($inlineNote,$deleteBase,$utf8)
$resetIndex=0
function Read-InlineNote {
 for($attempt=0;$attempt-lt30;$attempt++){
  try{return [IO.File]::ReadAllText($inlineNote,[Text.Encoding]::UTF8)}
  catch{if($_.Exception.GetBaseException()-isnot[IO.IOException]-or$attempt-eq29){throw};Start-Sleep -Milliseconds 20}
 }
}
function Reset-DeleteFixture([string]$Text=$deleteBase){
 if($null-ne(Get-InlineInput)-or$null-ne(Get-InlineInput 'Topic name')){Send-InlineKey 0x1B}
 $script:resetIndex++
 $script:otherMarker='Other preserved '+$resetIndex
 $next=$Text.Replace('Other unaffected',$otherMarker)
 [IO.File]::WriteAllText($inlineNote,$next,$utf8)
 Wait-Inline {$null-ne(Find-InlineControl ('Edit '+$otherMarker))} 'The next deletion fixture did not refresh.'
 return $next
}
function Expect-ItemFocus([string]$Value,[string]$Field='Item text'){
 Wait-InlineFocus $Field
 Wait-Inline {(Input-Value)-ceq$Value} ('Deletion did not focus the expected neighboring item: '+$Value)
}
function Clear-DeleteField([ushort]$Key=0x08,[string]$Name='Item text'){
 Wait-InlineFocus $Name;Send-InlineKey 0x41 0x11;Send-InlineKey $Key
 Wait-Inline {(Input-Value $Name)-ceq''} ('Selection deletion did not clear '+$Name)
}
function Undo-DeletedRow([string]$Title,[string]$Expected){
 if($null-ne(Get-InlineInput)){Send-InlineKey 0x1B}
 Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-ne(Find-InlineControl ('Edit '+$Title))} ('Undo did not restore '+$Title)
 Assert-Inline ((Read-InlineNote)-ceq$Expected) ('Undo restores the deleted '+$Title+' row with its original content and position')
}
try {
 $inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$opened=Get-InlineMain;$null-ne$opened-and[ShikeInlineInput]::IsWindowVisible([IntPtr]$opened.Current.NativeWindowHandle)} 'The isolated deletion app did not open.'
 Activate-Inline;Assert-NoEditorWindows

 if(!$TailOnly){
 $before=Read-InlineNote
 $nextTop=(Find-InlineControl 'Edit Next' ([System.Windows.Automation.ControlType]::Button)).Current.BoundingRectangle.Top
 Invoke-Inline 'Edit Target';Clear-DeleteField
 Assert-Inline (@(Group-Titles 'Plans').Count-eq3-and(Input-Value)-ceq'') 'Selecting text and pressing Backspace clears the text but retains its card'
 Send-InlineKey 0x08;Expect-ItemFocus 'Previous'
 Assert-RowOrder 'Plans' @('Previous','Next') 'A further Backspace removes the empty middle card'
 $movedTop=(Find-InlineControl 'Edit Next' ([System.Windows.Automation.ControlType]::Button)).Current.BoundingRectangle.Top
 Assert-Inline ($movedTop-lt$nextTop-20) 'The following card visibly moves upward into the removed row'
 Assert-Inline ((Group-Text 'Other').Contains('Other unaffected')) 'Deleting a card leaves the parallel topic unchanged'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-ne(Find-InlineControl 'Edit Target')} 'Ctrl+Z in the nonempty predecessor did not undo the deletion.'
 Assert-Inline ((Read-InlineNote)-ceq$before) 'Immediate Ctrl+Z from the focused predecessor restores the deleted row'

 Invoke-Inline 'Edit Target';Clear-DeleteField 0x2E
 Assert-Inline (@(Group-Titles 'Plans').Count-eq3-and(Input-Value)-ceq'') 'Selecting text and pressing Delete clears the text without deleting its card'
 Send-InlineKey 0x2E;Expect-ItemFocus 'Next'
 Assert-RowOrder 'Plans' @('Previous','Next') 'A further Delete removes the empty middle card and focuses its successor'
 Close-ReopenInline
 Assert-Inline ($null-eq(Find-InlineControl 'Edit Target')-and((Group-Titles 'Plans')-join'|')-ceq'Previous|Next') 'The keyboard-deleted card stays removed after closing and reopening'
 Undo-DeletedRow 'Target' $before

 Invoke-Inline 'Edit Previous';Clear-DeleteField;Send-InlineKey 0x08;Expect-ItemFocus 'Target'
 Assert-RowOrder 'Plans' @('Target','Next') 'Backspace on the first empty card falls forward to the next item'
 Undo-DeletedRow 'Previous' $before
 Invoke-Inline 'Edit Next';Clear-DeleteField 0x2E;Send-InlineKey 0x2E;Expect-ItemFocus 'Target'
 Assert-RowOrder 'Plans' @('Previous','Target') 'Delete on the last empty card falls back to the preceding item'
 Undo-DeletedRow 'Next' $before

 Invoke-Inline 'Add to Plans';Wait-InlineFocus 'Item text'
 $beforeBlankEnter=Read-InlineNote
 Send-InlineKey 0x0D;Wait-InlineFocus 'Item text'
 Assert-RowOrder 'Plans' @('Previous','Target','Next','','') 'Enter on an empty card still adds a following blank card'
 Send-InlineKey 0x08;Expect-ItemFocus ''
 Assert-Inline ((Read-InlineNote)-ceq$beforeBlankEnter) 'Backspace can remove the newly added blank while retaining the preceding blank'
 Send-InlineKey 0x08 0x11
 Assert-Inline ((Read-InlineNote)-ceq$beforeBlankEnter) 'Ctrl+Backspace on an empty field does not remove its card'
 Send-InlineKey 0x2E 0x11
 Assert-Inline ((Read-InlineNote)-ceq$beforeBlankEnter) 'Ctrl+Delete on an empty field does not remove its card'
 $before=Reset-DeleteFixture

 Invoke-Inline 'Edit Watch target';Clear-DeleteField;Send-InlineKey 0x08
 Assert-Inline (@(Group-Titles 'Watching list').Count-eq2-and(Input-Value)-ceq''-and(Input-Value 'Item link')-ceq'https://example.net/delete-check') 'An empty Watching name is protected while its link still has content'
 Send-InlineKey 0x09;Wait-InlineFocus 'Item link';Clear-DeleteField 0x08 'Item link'
 Assert-Inline (@(Group-Titles 'Watching list').Count-eq2-and(Input-Value 'Item link')-ceq'') 'Clearing the last Watching field retains its card until another deletion key'
 Send-InlineKey 0x08;Expect-ItemFocus 'Watch next' 'Item link'
 Assert-RowOrder 'Watching list' @('Watch next') 'A further Backspace removes a Watching card only when both fields are empty'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {$null-ne(Find-InlineControl 'Edit Watch target')} 'Undo from the Watching successor did not restore the removed card.'
 Assert-Inline ((Read-InlineNote)-ceq$before) 'Immediate Undo restores the Watching name, link and original position'
 Invoke-Inline 'Edit Watch target';Send-InlineKey 0x09;Wait-InlineFocus 'Item link'
 Clear-DeleteField 0x2E 'Item link';Send-InlineKey 0x2E
 Assert-Inline (@(Group-Titles 'Watching list').Count-eq2-and(Input-Value)-ceq'Watch target'-and(Input-Value 'Item link')-ceq'') 'An empty Watching link is protected while its name still has content'
 Send-InlineKey 0x1B

 $withNote=$deleteBase.Replace('- [ ] Watch next',("  > Protected note`n- [ ] Watch next"))
 $beforeNote=Reset-DeleteFixture $withNote
 Invoke-Inline 'Edit Watch target';Clear-DeleteField
 Send-InlineKey 0x09;Wait-InlineFocus 'Item link';Clear-DeleteField 0x08 'Item link';Send-InlineKey 0x08
 Assert-Inline (@(Group-Titles 'Watching list').Count-eq2-and(Group-Text 'Watching list').Contains('Protected note')) 'A Watching card with a preserved note is not removed when both editable fields are empty'
 Send-InlineKey 0x1B
 Assert-Inline ((Read-InlineNote)-ceq$beforeNote) 'Cancelling protected-note edits retains all original metadata'
 }

 foreach($key in @(0x08,0x2E)){
  $backwards=$key-eq0x08
  $rows=if($backwards){"- [ ]`n- [ ]`n- [ ] Hold end"}else{"- [ ] Hold start`n- [ ]`n- [ ]"}
  $heldFixture=[regex]::Replace($deleteBase,'(?ms)(^## Plans\r?\n).*?(?=^## Other)',('$1'+$rows+"`n`n"))
  $heldBefore=Reset-DeleteFixture $heldFixture
  Invoke-Inline 'Edit empty item 2 in Plans';Wait-InlineFocus 'Item text'
  if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Foreground left before the held-key test.'}
  try{
   [ShikeInlineInput]::Key([ushort]$key,$false)
   Wait-Inline {@(Group-Titles 'Plans').Count-eq2} 'The first held deletion key did not remove one blank.'
   Expect-ItemFocus ''
   foreach($repeat in 1..4){
    if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Foreground left during the held-key test.'}
    [ShikeInlineInput]::Key([ushort]$key,$false);Start-Sleep -Milliseconds 90
   }
  }finally{[ShikeInlineInput]::Key([ushort]$key,$true)}
  $keyName=if($backwards){'Backspace'}else{'Delete'}
  Assert-Inline (@(Group-Titles 'Plans').Count-eq2-and(Input-Value)-ceq'') ('Holding '+$keyName+' removes only one card and preserves the neighboring blank')
  Send-InlineKey ([ushort]$key)
  Expect-ItemFocus $(if($backwards){'Hold end'}else{'Hold start'})
  Assert-Inline (@(Group-Titles 'Plans').Count-eq1) ('A new '+$keyName+' press after release can remove the next empty card')
 }

 $topicBase=$deleteBase.Replace('- [ ] Target',("- [ ] [Target](<https://example.net/topic-undo>)`n  > Topic metadata kept"))
 foreach($key in @(0x08,0x2E)){
  $before=Reset-DeleteFixture $topicBase
  Invoke-Inline 'Rename Plans';Clear-DeleteField ([ushort]$key) 'Topic name'
  Assert-Inline ((Read-InlineNote)-ceq$before-and$null-ne(Get-InlineInput 'Topic name')) 'Clearing a topic name with the keyboard retains its column and content'
  Send-InlineKey 0x08 0x11;Send-InlineKey 0x2E 0x11
  Assert-Inline ((Read-InlineNote)-ceq$before-and$null-ne(Get-InlineInput 'Topic name')) 'Ctrl+Backspace and Ctrl+Delete cannot remove an empty topic'
  if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Foreground left before topic held-key deletion.'}
  try{
   [ShikeInlineInput]::Key([ushort]$key,$false)
   Wait-Inline {$null-eq(Get-InlineInput 'Topic name')-and(Read-InlineNote)-notmatch'(?m)^## Plans\r?$'} 'An extra deletion key did not remove the topic.'
   foreach($repeat in 1..4){
    if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Foreground left during topic held-key deletion.'}
    [ShikeInlineInput]::Key([ushort]$key,$false);Start-Sleep -Milliseconds 90
   }
  }finally{[ShikeInlineInput]::Key([ushort]$key,$true)}
  Assert-Inline ((Read-InlineNote)-notmatch'Topic metadata kept|topic-undo'-and$null-eq(Find-InlineControl 'Rename Plans')) 'An additional deletion key removes the topic with all of its contents'
  Assert-Inline ($null-eq(Get-InlineInput 'Topic name')-and$null-ne(Find-InlineControl 'Rename Other')-and$null-ne(Find-InlineControl 'Rename Watching list')-and(Group-Text 'Other').Contains($otherMarker)) 'Holding the deletion key leaves neighboring topics intact and does not focus another heading'
  if($key-eq0x08){Send-InlineKey 0x5A 0x11}else{Invoke-Inline 'Undo (Ctrl+Z)'}
  Wait-Inline {$null-ne(Find-InlineControl 'Rename Plans')-and(Read-InlineNote)-ceq$before} 'Topic Undo did not restore its exact original contents and position.'
  Assert-Inline ((Read-InlineNote)-ceq$before) $(if($key-eq0x08){'Ctrl+Z restores the deleted topic name, rows, link, note and original position'}else{'The Undo button restores the deleted topic name, rows, link, note and original position'})
 }

 $before=Reset-DeleteFixture
 Invoke-Inline 'Rename Plans';Clear-DeleteField 0x08 'Topic name'
 Invoke-Inline 'Rename Other';Send-InlineKey 0x1B
 $unnamed=@(Empty-TopicKeys)
 Assert-Inline ($unnamed.Count-eq1-and@(Group-Titles $unnamed[0]).Count-eq3) 'Leaving a cleared topic title preserves the unnamed column and all its rows'

 $before=Reset-DeleteFixture
 Invoke-Inline 'New topic';Wait-InlineFocus 'Topic name'
 $newTopicBefore=Read-InlineNote;$unnamed=@(Empty-TopicKeys)
 Send-InlineKey 0x2E
 Wait-Inline {$null-eq(Get-InlineInput 'Topic name')-and@(Empty-TopicKeys).Count-eq0} 'A new empty topic was not deleted by Delete.'
 Assert-Inline ((Read-InlineNote)-ceq$before) 'A newly created unnamed topic can be removed immediately'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {(Read-InlineNote)-ceq$newTopicBefore} 'Undo did not restore the newly deleted blank topic.'
 Assert-Inline (@(Empty-TopicKeys).Count-eq1-and(Empty-TopicKeys)-contains$unnamed[0]) 'Undo restores the same unnamed topic without creating a different one'

 $before=Reset-DeleteFixture
 Invoke-Inline 'Rename Watching list';Clear-DeleteField 0x08 'Topic name'
 Send-InlineKey 0x08;Send-InlineKey 0x2E
 Assert-Inline ((Read-InlineNote)-ceq$before-and$null-ne(Get-InlineInput 'Topic name')) 'The fixed Watching column remains when its empty heading receives Backspace or Delete'
 Send-InlineKey 0x1B
 foreach($lastGroup in @('Plans','Watching list')){
  $lastFixture=[regex]::Replace($deleteBase,('(?ms)(^## '+[regex]::Escape($lastGroup)+'\r?\n).*?(?=^## |\z)'),('$1'+"- [ ]`n`n"))
  $lastBefore=Reset-DeleteFixture $lastFixture
  Invoke-Inline ('Edit empty item 1 in '+$lastGroup);Send-InlineKey 0x2E
  Wait-Inline {@(Group-Titles $lastGroup).Count-eq0-and$null-eq(Get-InlineInput)} 'Deleting the final blank did not leave the column empty.'
  $addCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,('Add to '+$lastGroup))
  $addControls=(Get-InlineMain).FindAll([System.Windows.Automation.TreeScope]::Descendants,$addCondition)
  Assert-Inline ($addControls.Count-eq1-and$addControls[0].Current.BoundingRectangle.Width-lt110) ('Removing the final '+$lastGroup+' card leaves only its small header add button, with no replacement card')
  Close-ReopenInline
  Assert-Inline (@(Group-Titles $lastGroup).Count-eq0) ('The empty '+$lastGroup+' column stays empty after reopening')
  Invoke-Inline ('Add to '+$lastGroup)
  Assert-Inline (@(Group-Titles $lastGroup).Count-eq1-and(Input-Value)-ceq'') ('The '+$lastGroup+' header can explicitly add a new blank after all cards are removed')
 }
 $before=Reset-DeleteFixture
 Invoke-Inline 'Edit Target';Clear-DeleteField;Send-InlineKey 0x08;Expect-ItemFocus 'Previous'
 Send-InlineKey 0x1B;Restart-InlineResident
 Assert-RowOrder 'Plans' @('Previous','Next') 'A full process restart preserves keyboard deletion and the remaining order'
 Invoke-Inline 'Rename Plans';Clear-DeleteField 0x2E 'Topic name';Send-InlineKey 0x2E
 Wait-Inline {$null-eq(Get-InlineInput 'Topic name')-and$null-eq(Find-InlineControl 'Rename Plans')} 'Topic deletion did not finish before restart.'
 $deletedTopicText=Read-InlineNote
 Restart-InlineResident
 Assert-Inline ((Read-InlineNote)-ceq$deletedTopicText-and$null-eq(Find-InlineControl 'Rename Plans')-and$null-ne(Find-InlineControl 'Rename Other')) 'A full process restart preserves topic deletion and every neighboring column'
 Assert-NoEditorWindows
 ('PASS '+$passCount+' empty-card and topic keyboard assertions; isolated data: '+$inlineVault)
}finally{Stop-InlineHarness}
