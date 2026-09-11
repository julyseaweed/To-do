param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShikeReorderDpi {
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
'@
[ShikeReorderDpi]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
. (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ApplicationPath $ApplicationPath -HarnessOnly
Add-Type -AssemblyName System.Drawing
$fixture="# Current`n`n## Plans`n- [ ] Alpha`n- [ ] Beta`n- [ ] Gamma`n- [ ] Delta`n- [ ] Epsilon`n- [ ] Zeta`n- [ ] Eta`n- [ ] Theta`n- [ ] Iota`n- [ ] Kappa`n- [ ] Lambda`n- [ ] Mu`n`n## Watching list`n- [ ] [First watch](<https://example.net/first>)`n- [ ] [Second watch](<https://example.net/second>)`n  > Keep attached note.`n"
[IO.File]::WriteAllText($inlineNote,$fixture,$utf8)
$dragSettings=Get-Content -LiteralPath (Join-Path $inlineState 'settings.json') -Raw|ConvertFrom-Json
$dragSettings.Pinned=$true;$dragSettings.Height=500
[IO.File]::WriteAllText((Join-Path $inlineState 'settings.json'),($dragSettings|ConvertTo-Json),$utf8)
function Drag-Point([double]$X,[double]$Y) {
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'The reorder fixture lost foreground; pointer input stopped.'}
 [ShikeInlineInput]::SetCursorPos([int][Math]::Round($X),[int][Math]::Round($Y))|Out-Null
}
function Drag-Start([string]$Name) {
 Activate-Inline
 $rect=(Find-InlineControl $Name).Current.BoundingRectangle
 if($rect.IsEmpty-or$rect.Width-le0){throw ('Drag source unavailable: '+$Name)}
 $script:dragX=$rect.Left+$rect.Width*.35;$script:dragY=$rect.Top+$rect.Height*.5
 Drag-Point $dragX $dragY
 [ShikeInlineInput]::MouseButton($false)
}
function Drag-To([double]$X,[double]$Y) {
 for($step=1;$step-le12;$step++){
  Drag-Point ($dragX+($X-$dragX)*$step/12) ($dragY+($Y-$dragY)*$step/12)
  Start-Sleep -Milliseconds 20
 }
}
function Reorder-Scroll { (Find-InlineControl 'Items in Plans').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern) }
function Save-ReorderView([string]$Name) {
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'Capture skipped because the reorder fixture lost foreground.'}
 $bounds=(Get-InlineMain).Current.BoundingRectangle
 $bitmap=[Drawing.Bitmap]::new([int]$bounds.Width,[int]$bounds.Height)
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try{$graphics.CopyFromScreen([int]$bounds.Left,[int]$bounds.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $inlineVault ($Name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
}
try {
 $inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$null-ne(Get-InlineMain)} 'The isolated reorder fixture did not open.'
 Activate-Inline
 $target=(Find-InlineControl 'Edit Alpha').Current.BoundingRectangle
 try { Drag-Start 'Edit Delta';Drag-To ($target.Left+$target.Width*.35) ($target.Top+2);Save-ReorderView 'drag-insertion' } finally { [ShikeInlineInput]::MouseButton($true) }
 Wait-Inline {@(Group-Titles 'Plans')[0]-eq'Delta'} 'Dragging Delta above Alpha did not change order.'
 Assert-RowOrder 'Plans' @('Delta','Alpha','Beta','Gamma','Epsilon','Zeta','Eta','Theta','Iota','Kappa','Lambda','Mu') 'Dragging saved text moves its card upward by three positions'
 Assert-Inline ($null-eq(Get-InlineInput)) 'Releasing a dragged title does not open its text editor'
 $before=Read-InlineNote
 $target=(Find-InlineControl 'Edit Gamma').Current.BoundingRectangle
 try { Drag-Start 'Edit Delta';Drag-To ($target.Left+$target.Width*.35) ($target.Top+$target.Height-2) } finally { [ShikeInlineInput]::MouseButton($true) }
 Wait-Inline {@(Group-Titles 'Plans')[3]-eq'Delta'} 'Dragging Delta down did not change order.'
 Assert-RowOrder 'Plans' @('Alpha','Beta','Gamma','Delta','Epsilon','Zeta','Eta','Theta','Iota','Kappa','Lambda','Mu') 'Dragging downward places the card after the indicated target'
 Send-InlineKey 0x5A 0x11
 Wait-Inline {(Read-InlineNote)-ceq$before} 'Undo did not restore the previous dragged order.'
 Assert-Inline ((Read-InlineNote)-ceq$before) 'Ctrl+Z restores the exact previous card order'
 $target=(Find-InlineControl 'Edit Gamma').Current.BoundingRectangle
 try { Drag-Start 'Edit Delta';Drag-To ($target.Left+$target.Width*.35) ($target.Top+2);Send-InlineKey 0x1B } finally { [ShikeInlineInput]::MouseButton($true) }
 Start-Sleep -Milliseconds 160
 Assert-Inline ((Read-InlineNote)-ceq$before) 'Escape cancels a drag without changing notes'
 try { Drag-Start 'Edit Delta';Send-InlineKey 0x1B } finally { [ShikeInlineInput]::MouseButton($true) }
 Start-Sleep -Milliseconds 100
 Assert-Inline ((Read-InlineNote)-ceq$before-and$null-eq(Get-InlineInput)) 'Escape during the initial press cancels the pending text click as well as the drag'
 $other=(Find-InlineControl 'Items in Watching list').Current.BoundingRectangle
 try { Drag-Start 'Edit Delta';Drag-To ($other.Left+$other.Width*.5) ($other.Top+30) } finally { [ShikeInlineInput]::MouseButton($true) }
 Start-Sleep -Milliseconds 160
 Assert-Inline ((Read-InlineNote)-ceq$before) 'Dropping outside the source column leaves card order unchanged'
 Click-Inline 'Edit Alpha';Wait-InlineFocus 'Item text'
 Assert-Inline ((Input-Value)-ceq'Alpha') 'An ordinary click still starts editing directly'
 $field=Get-InlineInput 'Item text';$textBounds=$field.Current.BoundingRectangle
 # WPF starts selection inside its text-view inset, not the outer control edge.
 $script:dragX=$textBounds.Left+6;$script:dragY=$textBounds.Top+$textBounds.Height*.5
 Drag-Point $dragX $dragY
 try{[ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 30;Drag-To ($textBounds.Right-6) $dragY}finally{[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 60
 $selection=$field.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).GetSelection()
 Assert-Inline ($selection.Count-eq1-and$selection[0].GetText(-1)-ceq'Alpha'-and(Read-InlineNote)-ceq$before) 'Dragging within an active editor selects text without moving its card'
 Send-InlineKey 0x1B
 $first=(Find-InlineControl 'Edit First watch').Current.BoundingRectangle
 try { Drag-Start 'Edit Second watch';Drag-To ($first.Left+$first.Width*.35) ($first.Top+1) } finally { [ShikeInlineInput]::MouseButton($true) }
 Wait-Inline {(Group-Text 'Watching list')-match'(?s)Second watch.*First watch'} 'Watching cards did not reorder.'
 Assert-Inline ((Group-Text 'Watching list')-match'(?s)Second watch.*https://example.net/second.*Keep attached note.*First watch') 'Watching card moves its title, link and attached note together'
 (Reorder-Scroll).SetScrollPercent(-1,100)
 Start-Sleep -Milliseconds 200
 $viewport=(Find-InlineControl 'Items in Plans').Current.BoundingRectangle
 $scrollBefore=(Reorder-Scroll).Current.VerticalScrollPercent
 try {
  Drag-Start 'Edit Mu';Drag-To ($viewport.Left+$viewport.Width*.4) ($viewport.Top+8)
  Start-Sleep -Milliseconds 800
  Assert-Inline ((Reorder-Scroll).Current.VerticalScrollPercent-lt$scrollBefore-5) 'Dragging near the column top scrolls that column toward earlier cards'
 } finally { [ShikeInlineInput]::MouseButton($true) }
 Wait-Inline {[array]::IndexOf(@(Group-Titles 'Plans'),'Mu')-lt10} 'Autoscrolled drop did not move Mu toward the beginning.'
 $savedOrder=Read-InlineNote
 Restart-InlineResident
 Assert-Inline ((Read-InlineNote)-ceq$savedOrder) 'Reordered cards persist after a full restart'
 'PASS '+$passCount+' native reorder checks; isolated data: '+$inlineVault
} finally { Stop-InlineHarness }
