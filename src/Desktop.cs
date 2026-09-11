using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Shike {
    public static class Desktop {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        static string StartupCommand { get { return "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" --autostart"; } }
        public static bool StartupEnabled {
            get { using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && string.Equals(key.GetValue("To-do") as string, StartupCommand, StringComparison.OrdinalIgnoreCase); }
        }
        public static void SetStartup(bool enabled) {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (enabled) key.SetValue("To-do", StartupCommand); else key.DeleteValue("To-do", false);
            }
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(int ex,string className,string title,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr target, IntPtr first, IntPtr second, int mode);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr value);
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Window, After; public int X, Y, Width, Height; public uint Flags; }
        delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp, UIntPtr id, UIntPtr data);
        static readonly System.Collections.Generic.Dictionary<IntPtr,SubclassProc> ResizeHooks = new System.Collections.Generic.Dictionary<IntPtr,SubclassProc>();
        static readonly System.Collections.Generic.HashSet<IntPtr> MovingWindows = new System.Collections.Generic.HashSet<IntPtr>();
        [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint message, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        static readonly System.Collections.Generic.Dictionary<IntPtr,GlassHost> GlassHosts = new System.Collections.Generic.Dictionary<IntPtr,GlassHost>();
        sealed class GlassHost : IDisposable {
            readonly Window window;
            readonly IntPtr main, previousOwner;
            IntPtr background;
            GlassBackdrop renderer;
            bool syncing, disposed, regionBubble, shown;
            int regionWidth,regionHeight, lastLeft, lastTop;
            double regionScale;
            public bool Bubble;
            public GlassHost(Window w) {
                window=w; main=new WindowInteropHelper(w).Handle; previousOwner=GetWindowLongPtr(main,-8);
                try {
                    // The no-redirection surface samples the desktop directly on the GPU.
                    // The WPF window stays above it and draws crisp text and controls.
                    background=CreateWindowEx(0x082000A0,"Message","To-do glass",unchecked((int)0x80000000),0,0,1,1,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
                    if(background==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
                    int noCorners=1;DwmSetWindowAttribute(background,33,ref noCorners,4); // DWMWCP_DONOTROUND; the bubble has its own region.
                    renderer=new GlassBackdrop(background);
                    SetWindowLongPtr(main,-8,background);
                    window.IsVisibleChanged+=VisibilityChanged;
                    window.StateChanged+=WindowChanged;
                    window.Loaded+=Loaded;
                    window.Closed+=Closed;
                } catch {Dispose();throw;}
            }
            void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e){Sync();}
            void WindowChanged(object sender,EventArgs e){Sync();}
            void Loaded(object sender,RoutedEventArgs e){Sync();}
            void Closed(object sender,EventArgs e){GlassHosts.Remove(main);Dispose();}
            public void Sync(bool positionOnly = false) {
                if(syncing||disposed)return;
                syncing=true;
                try {
                    if(!window.IsVisible||!IsWindowVisible(main)||IsIconic(main)){if(shown)ShowWindow(background,0);shown=false;return;}
                    NativeRect r;if(!GetWindowRect(main,out r))return;
                    int width=r.Right-r.Left,height=r.Bottom-r.Top;
                    if(width<=0||height<=0)return;
                    var source=HwndSource.FromHwnd(main);if(source==null||source.CompositionTarget==null)return;
                    double scale=source.CompositionTarget.TransformToDevice.M11;
                    // A move does not change the material, shape, or stacking.
                    // Leave those alone so the native drag loop only moves pixels.
                    if(positionOnly&&shown&&regionWidth==width&&regionHeight==height&&regionBubble==Bubble&&Math.Abs(regionScale-scale)<=0.001){
                        if(lastLeft!=r.Left||lastTop!=r.Top){
                            if(SetWindowPos(background,IntPtr.Zero,r.Left,r.Top,0,0,0x215)){lastLeft=r.Left;lastTop=r.Top;}
                        }
                        return;
                    }
                    renderer.Resize(width,height,scale);
                    if(GetWindowLongPtr(main,-8)!=background)SetWindowLongPtr(main,-8,background);
                    bool top=(GetWindowLong(main,-20)&8)!=0;
                    if(top!=((GetWindowLong(background,-20)&8)!=0))SetWindowPos(background,new IntPtr(top?-1:-2),0,0,0,0,0x213);
                    if(regionWidth!=width||regionHeight!=height||regionBubble!=Bubble||Math.Abs(regionScale-scale)>0.001){
                        ApplyRegion(main,Bubble,width,height,scale);
                        ApplyRegion(background,Bubble,width,height,scale);
                        regionWidth=width;regionHeight=height;regionBubble=Bubble;regionScale=scale;
                    }
                    // Place the material immediately below its content without activation.
                    if(SetWindowPos(background,main,r.Left,r.Top,width,height,0x250)){lastLeft=r.Left;lastTop=r.Top;shown=true;}
                } finally {syncing=false;}
            }
            public void Dispose() {
                if(disposed)return;disposed=true;
                window.IsVisibleChanged-=VisibilityChanged;window.StateChanged-=WindowChanged;window.Loaded-=Loaded;window.Closed-=Closed;
                if(GetWindowLongPtr(main,-8)==background)SetWindowLongPtr(main,-8,previousOwner);
                if(renderer!=null){renderer.Dispose();renderer=null;}
                if(background!=IntPtr.Zero){DestroyWindow(background);background=IntPtr.Zero;}
            }
        }
        public static bool Glass(Window w) {
            var hwnd = new WindowInteropHelper(w).Handle;
            if(GlassHosts.ContainsKey(hwnd))return true;
            HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
            var margin = new Margins { Left = 0, Right = 0, Top = 0, Bottom = 0 };
            DwmExtendFrameIntoClientArea(hwnd, ref margin);
            int corners = 1, light = 1, border = unchecked((int)0xFFFFFFFE), backdrop = 1; // DWMWCP_DONOTROUND
            DwmSetWindowAttribute(hwnd, 33, ref corners, 4);
            DwmSetWindowAttribute(hwnd, 20, ref light, 4);
            DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
            // Keep any owned utility window's native owner intact.
            if(w.Owner!=null)return false;
            try {GlassHosts.Add(hwnd,new GlassHost(w));return true;}
            catch(COMException) {return false;}
            catch(TypeLoadException) {return false;}
            catch(System.ComponentModel.Win32Exception) {return false;}
        }
        public static double? GlassLuminance(Window window) {
            if (!window.IsVisible || window.WindowState == WindowState.Minimized || window.OwnedWindows.Count > 0 || IsInteracting(window)) return null;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (!window.Topmost && GetForegroundWindow() != hwnd) return null;
            // Sample this widget's header and footer glass, away from tinted cards.
            // No screenshots, text capture, storage, or network access.
            IntPtr dc = GetDC(IntPtr.Zero); if (dc == IntPtr.Zero) return null;
            try {
                var values = new System.Collections.Generic.List<double>();
                foreach (double x in new[] { 0.34, 0.55, 0.74 }) foreach (double y in new[] { 0.10, 0.92 }) {
                    var p = window.PointToScreen(new System.Windows.Point(window.ActualWidth * x, window.ActualHeight * y));
                    uint c = GetPixel(dc, (int)p.X, (int)p.Y); if (c == 0xFFFFFFFF) continue;
                    values.Add((0.2126 * (c & 255) + 0.7152 * ((c >> 8) & 255) + 0.0722 * ((c >> 16) & 255)) / 255.0);
                }
                values.Sort(); return values.Count == 0 ? (double?)null : values[values.Count / 2];
            } finally { ReleaseDC(IntPtr.Zero, dc); }
        }
        public static void Chrome(Window w) {
            w.WindowStyle = WindowStyle.None; w.AllowsTransparency = true; w.Background = System.Windows.Media.Brushes.Transparent;
            // Native hit tests below provide resizing. WindowChrome must not
            // replace the circular region when a size change finishes.
        }
        public static bool IsInteracting(Window window) { return MovingWindows.Contains(new WindowInteropHelper(window).Handle); }
        public static void EnableCornerResize(Window window, Func<System.Windows.Size, bool> beginCorner = null, Func<System.Windows.Size, System.Windows.Size> resizeCorner = null, Action<System.Windows.Size> endCorner = null, Action endMoveOrEdgeResize = null) {
            var hwnd = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            NativeRect originalBounds = new NativeRect();
            bool haveOriginalBounds = false, cornerAttempted = false, cornerActive = false;
            SubclassProc callback = null;
            callback = delegate(IntPtr h, uint message, IntPtr wp, IntPtr lp, UIntPtr id, UIntPtr data) {
                if (message == 0x82) { RemoveWindowSubclass(h,callback,id); ResizeHooks.Remove(h); MovingWindows.Remove(h); }
                if (message == 0x231) { // WM_ENTERSIZEMOVE, including ordinary moves and edge resizing.
                    MovingWindows.Add(h);
                    cornerAttempted = cornerActive = false;
                    haveOriginalBounds = resizeCorner != null && GetWindowRect(h, out originalBounds);
                }
                if (message == 0x214 && resizeCorner != null && window.WindowState == WindowState.Normal && window.ResizeMode != ResizeMode.NoResize && lp != IntPtr.Zero) {
                    int edge = wp.ToInt32(); // WM_SIZING uses WMSZ values, not WM_NCHITTEST values.
                    if (edge == 4 || edge == 5 || edge == 7 || edge == 8) {
                        if (!haveOriginalBounds) haveOriginalBounds = GetWindowRect(h, out originalBounds);
                        if (haveOriginalBounds && !source.IsDisposed && source.CompositionTarget != null) {
                            var transform = source.CompositionTarget.TransformFromDevice;
                            if (!cornerAttempted) {
                                cornerAttempted = true;
                                // Begin runs once, before the first corner rectangle is applied.
                                // A rejected pending editor keeps the original rectangle intact.
                                var start = transform.Transform(new System.Windows.Point(originalBounds.Right - originalBounds.Left, originalBounds.Bottom - originalBounds.Top));
                                cornerActive = beginCorner == null || beginCorner(new System.Windows.Size(start.X, start.Y));
                            }
                            var rectangle = originalBounds;
                            if (cornerActive) {
                                rectangle = (NativeRect)Marshal.PtrToStructure(lp, typeof(NativeRect));
                                var requested = transform.Transform(new System.Windows.Point(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top));
                                // Pass the proposed native rectangle rather than ActualWidth/Height,
                                // which still describe the previous frame during WM_SIZING.
                                var accepted = resizeCorner(new System.Windows.Size(requested.X, requested.Y));
                                var physical = source.CompositionTarget.TransformToDevice.Transform(new System.Windows.Point(accepted.Width, accepted.Height));
                                int width = Math.Max(1, (int)Math.Round(physical.X));
                                int height = Math.Max(1, (int)Math.Round(physical.Y));
                                rectangle = AnchorCorner(originalBounds, width, height, edge);
                            }
                            Marshal.StructureToPtr(rectangle, lp, false);
                            return new IntPtr(1);
                        }
                    }
                }
                if (message == 0x232) {
                    var result=DefSubclassProc(h,message,wp,lp);MovingWindows.Remove(h);
                    bool finish = cornerActive;
                    haveOriginalBounds = cornerAttempted = cornerActive = false;
                    // End reconciles against the actual final native rectangle. Esc can
                    // restore it without dispatching an ordinary WPF keyboard event.
                    NativeRect finalBounds;
                    if (finish && endCorner != null && !source.IsDisposed && source.CompositionTarget != null && GetWindowRect(h, out finalBounds)) {
                        var finalSize = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(finalBounds.Right - finalBounds.Left, finalBounds.Bottom - finalBounds.Top));
                        endCorner(new System.Windows.Size(finalSize.X, finalSize.Y));
                    }
                    // Persist ordinary moves and edge resizes once, on release.
                    // Corner scaling commits after reconciling its rendered scale.
                    if (!finish && endMoveOrEdgeResize != null) endMoveOrEdgeResize();
                    GlassHost host;if(GlassHosts.TryGetValue(h,out host))host.Sync();return result;
                }
                if(message==0x47||message==0x18||message==0x7D){
                    bool positionOnly=false;
                    if(message==0x47&&lp!=IntPtr.Zero){
                        var position=(WindowPosition)Marshal.PtrToStructure(lp,typeof(WindowPosition));
                        // SWP_NOSIZE | SWP_NOZORDER, without frame/show/hide changes.
                        positionOnly=(position.Flags&0xE5)==0x5;
                    }
                    var result=DefSubclassProc(h,message,wp,lp);GlassHost host;
                    if(GlassHosts.TryGetValue(h,out host))host.Sync(positionOnly);return result;
                }
                if (message != 0x84 || window.WindowState != WindowState.Normal || window.ResizeMode == ResizeMode.NoResize) return DefSubclassProc(h,message,wp,lp);
                NativeRect r; if (!GetWindowRect(h, out r)) return DefSubclassProc(h,message,wp,lp);
                long packed = lp.ToInt64();
                int x = (short)(packed & 0xffff), y = (short)((packed >> 16) & 0xffff);
                var p = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(x-r.Left,y-r.Top));
                var size = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(r.Right-r.Left,r.Bottom-r.Top));
                // Only inspect WPF descendants inside the enlarged corner
                // zones. Ordinary edges and move messages keep their fast path.
                IInputElement input = IsInteriorResizeCorner(p, size) ? window.InputHitTest(p) : null;
                int hit = ResizeHit(p, size, input);
                if (hit != 0) return new IntPtr(hit);
                return DefSubclassProc(h,message,wp,lp);
            };
            // Handle diagonal resize zones before WPF window chrome.
            if (SetWindowSubclass(hwnd,callback,new UIntPtr(1),UIntPtr.Zero)) ResizeHooks[hwnd] = callback;
        }
        static NativeRect AnchorCorner(NativeRect original, int width, int height, int edge) {
            bool left = edge == 4 || edge == 7, top = edge == 4 || edge == 5;
            return new NativeRect {
                Left = left ? original.Right - width : original.Left,
                Right = left ? original.Right : original.Left + width,
                Top = top ? original.Bottom - height : original.Top,
                Bottom = top ? original.Bottom : original.Top + height
            };
        }
        static bool IsInteriorResizeCorner(System.Windows.Point p, System.Windows.Point size) {
            return p.X >= 7 && p.Y >= 7 && p.X < size.X - 7 && p.Y < size.Y - 7 &&
                (p.X < 24 || p.X >= size.X - 24) && (p.Y < 24 || p.Y >= size.Y - 24);
        }
        static int ResizeHit(System.Windows.Point p, System.Windows.Point size, IInputElement input) {
            if (IsInteriorResizeCorner(p, size)) {
                // Inner corner affordances must not steal the Close button or
                // an action near the footer. The outer 7-DIP edge still resizes.
                for (var node = input as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                    if (node is ButtonBase || node is TextBoxBase || node is Thumb) return 0;
            }
            const double corner = 24, edge = 7;
            if (p.X < corner && p.Y < corner) return 13;
            if (p.X >= size.X - corner && p.Y < corner) return 14;
            if (p.X < corner && p.Y >= size.Y - corner) return 16;
            if (p.X >= size.X - corner && p.Y >= size.Y - corner) return 17;
            if (p.X < edge) return 10;
            if (p.X >= size.X - edge) return 11;
            if (p.Y < edge) return 12;
            if (p.Y >= size.Y - edge) return 15;
            return 0;
        }
        public static Rect WorkArea(Window window) {
            var hwnd = new WindowInteropHelper(window).Handle;
            var area = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
            var transform = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformFromDevice;
            return new Rect(transform.Transform(new System.Windows.Point(area.Left,area.Top)),transform.Transform(new System.Windows.Point(area.Right,area.Bottom)));
        }
        public static void Move(Window window, System.Windows.Point position) {
            var hwnd = new WindowInteropHelper(window).Handle;
            var p = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice.Transform(position);
            SetWindowPos(hwnd,IntPtr.Zero,(int)Math.Round(p.X),(int)Math.Round(p.Y),0,0,0x15);
        }
        public static void ClipBubble(Window window, bool bubble) {
            var hwnd = new WindowInteropHelper(window).Handle;
            GlassHost host;
            if(GlassHosts.TryGetValue(hwnd,out host)){host.Bubble=bubble;host.Sync();return;}
            double scale = HwndSource.FromHwnd(hwnd).CompositionTarget.TransformToDevice.M11;
            NativeRect r;if(GetWindowRect(hwnd,out r))ApplyRegion(hwnd,bubble,r.Right-r.Left,r.Bottom-r.Top,scale);
        }
        static void ApplyRegion(IntPtr hwnd,bool bubble,int width,int height,double scale) {
            if(!bubble){
                // Match the expanded WPF surface's restrained 12-DIP radius.
                // Both native windows share it; the bubble keeps its own mask.
                int diameter=(int)Math.Round(24*scale);
                IntPtr rounded=CreateRoundRectRgn(0,0,width+1,height+1,diameter,diameter);
                if(SetWindowRgn(hwnd,rounded,true)==0)DeleteObject(rounded);return;
            }
            IntPtr circle = CreateEllipticRgn(0,(int)(8*scale),(int)(56*scale),(int)(64*scale));
            IntPtr close = CreateEllipticRgn((int)(44*scale),0,(int)(64*scale),(int)(20*scale));
            CombineRgn(circle,circle,close,2); DeleteObject(close);
            // Windows owns a region after a successful SetWindowRgn.
            if (SetWindowRgn(hwnd,circle,true) == 0) DeleteObject(circle);
        }
        public static Icon MakeIcon() {
            using (var stream = typeof(Desktop).Assembly.GetManifestResourceStream("Shike.Icon"))
            using (var source = new Icon(stream, 32, 32)) return (Icon)source.Clone();
        }
        public static void Open(string link, string vault) {
            string resolved = Links.Resolve(link, vault);
            try { Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) {
                if (!resolved.StartsWith("obsidian://", StringComparison.OrdinalIgnoreCase)) throw;
                throw new InvalidOperationException("Open Obsidian once to register its links, then try again.");
            }
        }
        public static void Screenshot(Window w, string path) {
            if (GetForegroundWindow() != new WindowInteropHelper(w).Handle) throw new InvalidOperationException("Capture cancelled: the app is not in the foreground.");
            var source = PresentationSource.FromVisual(w);
            var scale = source.CompositionTarget.TransformToDevice;
            var pos = w.PointToScreen(new System.Windows.Point(0, 0));
            using (var bmp = new Bitmap((int)(w.ActualWidth * scale.M11), (int)(w.ActualHeight * scale.M22))) {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen((int)pos.X, (int)pos.Y, 0, 0, bmp.Size);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
