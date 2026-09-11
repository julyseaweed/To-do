# Owned WPF windows are not consistently exposed as UIAutomation root children,
# and Process.MainWindowHandle may select the native glass owner instead.
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
if(-not ('ShikeWindowDiscovery' -as [type])) {
 Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ShikeWindowDiscovery {
 delegate bool EnumProc(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr param);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
 public static IntPtr Find(int processId) {
  return FindTitle(processId,"To-do");
 }
 public static IntPtr FindTitle(int processId,string expectedTitle) {
  IntPtr result=IntPtr.Zero;
  EnumWindows(delegate(IntPtr hwnd,IntPtr unused){
   uint pid;GetWindowThreadProcessId(hwnd,out pid);
   if(pid!=processId)return true;
   var title=new StringBuilder(256);GetWindowText(hwnd,title,title.Capacity);
   if(!String.Equals(title.ToString(),expectedTitle,StringComparison.Ordinal))return true;
   result=hwnd;return false;
  },IntPtr.Zero);
  return result;
 }
}
'@
}
function Get-ShikeWindow([int]$AppId) {
 $handle=[ShikeWindowDiscovery]::Find($AppId)
 if($handle-ne[IntPtr]::Zero){[System.Windows.Automation.AutomationElement]::FromHandle($handle)}
}
