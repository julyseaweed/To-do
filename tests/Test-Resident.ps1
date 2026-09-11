param([string]$ApplicationPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. (Join-Path $PSScriptRoot 'Get-AppWindow.ps1')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ResidentProbe {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
if(!$ApplicationPath){$ApplicationPath=Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\To-do.exe'}
$residentVault=Join-Path $env:TEMP ('shike-resident-'+[Guid]::NewGuid().ToString('N'))
$residentState=Join-Path $residentVault 'state'
New-Item -ItemType Directory -Path $residentState -Force | Out-Null
@{Vault=$residentVault;DesignVersion=2;Left=16;Top=16;Width=440;Height=280;Transparency=86;Pinned=$false;Zoom=1} | ConvertTo-Json | Set-Content (Join-Path $residentState 'settings.json')
$residentApp=Start-Process $applicationPath -ArgumentList @('--state-dir',$residentState,'--autostart') -PassThru
function Find-Resident {
 Get-ShikeWindow -AppId $residentApp.Id
}
function Invoke-Resident($label) { (Find-Resident).FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$label)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Relaunch {
 $second=Start-Process $applicationPath -ArgumentList @('--state-dir',$residentState) -PassThru
 if(!$second.WaitForExit(2000)) { throw 'A second instance stayed open.' }
 Start-Sleep -Milliseconds 350
 if ($null -eq (Find-Resident)) { throw 'Relaunch did not restore the hidden panel.' }
}
try {
 for($attempt=0;$attempt-lt 50;$attempt++){Start-Sleep -Milliseconds 100;if($null-ne(Find-Resident)){break}}
 $errorPath=Join-Path $residentState 'error.log';if(Test-Path $errorPath){throw(Get-Content $errorPath -Raw)}
 $residentHandle=[IntPtr](Find-Resident).Current.NativeWindowHandle
 Invoke-Resident 'Close panel'
 Start-Sleep -Milliseconds 100
 if($residentApp.HasExited -or [ResidentProbe]::IsWindowVisible($residentHandle)) { throw 'Close did not hide resident app' }
 'PASS X hides the resident panel and keeps the app alive'
 Relaunch
 'PASS Relaunch restores the same resident process'
 if((Find-Resident).Current.BoundingRectangle.Height -gt 200) { Invoke-Resident 'Collapse / Expand'; Start-Sleep -Milliseconds 250 }
 Relaunch
 if((Find-Resident).Current.BoundingRectangle.Height -lt 500) {throw 'Relaunch did not expand compact window'}
 'PASS Relaunch expands a collapsed panel'
 Invoke-Resident 'Collapse / Expand';Start-Sleep -Milliseconds 330
 $residentHandle=[IntPtr](Find-Resident).Current.NativeWindowHandle
 Invoke-Resident 'Close bubble';Start-Sleep -Milliseconds 150
 if($residentApp.HasExited -or [ResidentProbe]::IsWindowVisible($residentHandle)){throw 'Closing the bubble did not hide the resident app'}
 'PASS The small X hides the bubble and keeps the app alive'
 Relaunch
 if((Find-Resident).Current.BoundingRectangle.Height -lt 500){throw 'Relaunch from a hidden bubble did not restore the full panel'}
 'PASS Relaunch from a hidden bubble restores the full panel'
 $quitRequest=Start-Process -FilePath $ApplicationPath -ArgumentList @('--state-dir',('"'+$residentState+'"'),'--quit') -WindowStyle Hidden -PassThru
 if(!$quitRequest.WaitForExit(2000)){Stop-Process -Id $quitRequest.Id;throw 'The resident quit request did not finish.'}
 if(!$residentApp.WaitForExit(2000)) {throw 'Quit did not exit'}
 'PASS The quit command ends the resident process'
 $errorPath=Join-Path $residentState 'error.log'
 if(Test-Path $errorPath) {throw (Get-Content $errorPath -Raw)}
} finally { if(!$residentApp.HasExited) { Stop-Process -Id $residentApp.Id } }
