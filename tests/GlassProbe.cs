using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Shell;
using Shike;
class GlassProbe {
 [StructLayout(LayoutKind.Sequential)] struct Margins {public int L,R,T,B;}
 [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr w,ref Margins m);
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr w,int a,ref int v,int size);
 [STAThread] static void Main(string[] args) {
  var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
  var back=new Window {Title="Glass reference",Left=16,Top=520,Width=620,Height=280,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true};
  var canvas=new Canvas {Background=new LinearGradientBrush(Color.FromRgb(232,218,235),Color.FromRgb(118,183,206),0)};
  for(int x=0;x<620;x+=12) canvas.Children.Add(new Border {Width=6,Height=280,Background=x%24==0?Brushes.White:Brushes.Black,Margin=new Thickness(x,0,0,0),Opacity=0.6});
  canvas.Children.Add(new TextBlock {Text="BACKGROUND DETAIL 123",Foreground=Brushes.Black,FontSize=28,FontWeight=FontWeights.Bold,Margin=new Thickness(24,120,0,0)});back.Content=canvas;
  bool external=args.Length>2;
  if(!external)back.Show();
  var w=new Window {Title="Glass test",Left=16,Top=520,Width=620,Height=280,Topmost=true,ShowInTaskbar=true,Owner=external?null:back};
  Desktop.Chrome(w);w.ResizeMode=ResizeMode.NoResize;
  bool native=args.Length>1;
  if(native){w.AllowsTransparency=false;WindowChrome.GetWindowChrome(w).GlassFrameThickness=new Thickness(-1);}
  var panel=new Border {CornerRadius=new CornerRadius(22),BorderBrush=Theme.Edge(),BorderThickness=new Thickness(1)};
  var label=new TextBlock {Text="FOREGROUND TEXT",Foreground=Brushes.White,FontSize=24,FontWeight=FontWeights.Bold,Margin=new Thickness(24)};panel.Child=label;w.Content=panel;
  w.SourceInitialized+=delegate {
   var h=new WindowInteropHelper(w).Handle;bool blur;
   if(native){var margins=new Margins{L=-1,R=-1,T=-1,B=-1};DwmExtendFrameIntoClientArea(h,ref margins);HwndSource.FromHwnd(h).CompositionTarget.BackgroundColor=Colors.Transparent;int type=3,dark=1;DwmSetWindowAttribute(h,20,ref dark,4);blur=DwmSetWindowAttribute(h,38,ref type,4)==0;}
   else blur=Desktop.Glass(w);
   panel.Background=Theme.GlassSurface(45,blur);File.WriteAllText(args[0],"HWND="+h+" BLUR="+blur);
  };
  var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};timer.Tick+=delegate{timer.Stop();w.Close();back.Close();};timer.Start();app.MainWindow=w;app.Run(w);
 }
}
