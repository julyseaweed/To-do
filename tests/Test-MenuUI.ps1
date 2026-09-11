param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShikeMenuDpi {
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
'@
[ShikeMenuDpi]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
. (Join-Path $PSScriptRoot 'Test-InlineUI.ps1') -ApplicationPath $ApplicationPath -HarnessOnly
Add-Type -AssemblyName System.Drawing
$menuState=Get-Content -LiteralPath (Join-Path $inlineState 'settings.json') -Raw|ConvertFrom-Json
$menuState.Pinned=$true
[IO.File]::WriteAllText((Join-Path $inlineState 'settings.json'),($menuState|ConvertTo-Json),$utf8)
function Menu-Slider { Find-InlineControl 'Glass transparency' ([Windows.Automation.ControlType]::Slider) -Anywhere }
function Menu-Value { $control=Menu-Slider;if($null-eq$control){return -1};$control.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value }
function Menu-Point([double]$X,[double]$Y) {
 if(![ShikeInlineInput]::ForegroundBelongsTo($inlineApp.Id)){throw 'The menu test lost foreground; pointer input cancelled.'}
 [ShikeInlineInput]::SetCursorPos([int][Math]::Round($X),[int][Math]::Round($Y))|Out-Null
}
function Click-MenuPoint([double]$X,[double]$Y) {
 Menu-Point $X $Y
 try{[ShikeInlineInput]::MouseButton($false);Start-Sleep -Milliseconds 40}finally{[ShikeInlineInput]::MouseButton($true)}
 Start-Sleep -Milliseconds 100
}
try {
 $inlineApp=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$inlineState+'"')) -PassThru
 Wait-Inline {$null-ne(Get-InlineMain)} 'The isolated menu fixture did not open.'
 Activate-Inline
 Invoke-Inline 'More'
 Wait-Inline {$null-ne(Menu-Slider)} 'The transparency slider is missing.'
 Start-Sleep -Milliseconds 500
 $range=(Menu-Slider).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current
 Assert-Inline ($range.Minimum-eq35-and$range.Maximum-eq95-and$range.Value-eq45) 'Native popup exposes the requested 35–95 range and saved 45 value'
 $bounds=(Menu-Slider).Current.BoundingRectangle
 Click-MenuPoint ($bounds.Left+$bounds.Width*.5) ($bounds.Top+$bounds.Height*.5)
 Assert-Inline ([Math]::Abs((Menu-Value)-65)-le1) 'Clicking the center sets transparency to 65 without closing the menu'
 Send-InlineKey 0x24
 Wait-Inline {(Menu-Value)-eq35} 'Home did not select minimum transparency.'
 Send-InlineKey 0x27
 Wait-Inline {(Menu-Value)-eq36} 'Right arrow did not advance transparency by one percent.'
 Send-InlineKey 0x23
 Wait-Inline {(Menu-Value)-eq95} 'End did not select maximum transparency.'
 Assert-Inline ($null-ne(Menu-Slider)) 'Home, End and arrow keys operate the slider while its popup remains open'
 $bounds=(Menu-Slider).Current.BoundingRectangle
 $scale=$bounds.Width/200
 $right=$bounds.Right-7*$scale
 $left=$bounds.Left+7*$scale
 Menu-Point $right ($bounds.Top+$bounds.Height*.5)
 try {
  [ShikeInlineInput]::MouseButton($false)
  for($step=1;$step-le10;$step++) { Menu-Point ($right+($left-$right)*$step/10) ($bounds.Top+$bounds.Height*.5);Start-Sleep -Milliseconds 15 }
 } finally { [ShikeInlineInput]::MouseButton($true) }
 Wait-Inline {(Menu-Value)-eq35} 'Dragging the circular thumb to the left did not reach 35.'
 Assert-Inline ((Menu-Value)-eq35) 'The native circular slider stays open throughout a full-range drag to its endpoint'
 Click-MenuPoint ($left+($right-$left)/6) ($bounds.Top+$bounds.Height*.5)
 Wait-Inline {(Menu-Value)-eq45} 'One sixth of the usable rail did not select 45.'
 Assert-Inline ((Menu-Value)-eq45) '45 percent sits one sixth of the way through the 35–95 range'
 $popup=Find-InlineControl 'Always on top' -Anywhere
 while($null-ne$popup-and$popup.Current.ControlType-ne[Windows.Automation.ControlType]::Menu){$popup=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($popup)}
 if($null-eq$popup){throw 'Native menu bounds unavailable for visual verification.'}
 $menuRect=$popup.Current.BoundingRectangle
 $capture=[Drawing.Bitmap]::new([int]($menuRect.Width+8),[int]($menuRect.Height+8))
 $graphics=[Drawing.Graphics]::FromImage($capture)
 try {
  $graphics.CopyFromScreen([int]($menuRect.Left-4),[int]($menuRect.Top-4),0,0,$capture.Size)
  $capture.Save((Join-Path $inlineVault 'native-menu.png'),[Drawing.Imaging.ImageFormat]::Png)
 } finally { $graphics.Dispose();$capture.Dispose() }
 Send-InlineKey 0x1B
 Wait-Inline {$null-eq(Menu-Slider)} 'Escape did not dismiss the menu.'
 $saved=Get-Content -LiteralPath (Join-Path $inlineState 'settings.json') -Raw|ConvertFrom-Json
 Assert-Inline ($saved.Transparency-eq45) 'Dismissing the popup preserves the final slider setting'
 Assert-Inline ((Read-InlineNote)-ceq$fixture) 'Menu interactions leave all fixture notes unchanged'
 'PASS '+$passCount+' native menu checks; isolated data: '+$inlineVault
} finally { Stop-InlineHarness }
