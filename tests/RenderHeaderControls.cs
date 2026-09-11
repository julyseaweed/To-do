using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shike;

// Renders detached controls and creates hidden HWNDs only. No window is shown.
class RenderHeaderControls {
    static int checks;
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static T Field<T>(MainWindow window, string name) {
        return (T)typeof(MainWindow).GetField(name, PrivateInstance).GetValue(window);
    }
    static object Call(MainWindow window, string name) {
        return typeof(MainWindow).GetMethod(name, PrivateInstance).Invoke(window, null);
    }
    static MainWindow NewWindow(string state, bool pinned) {
        var store = new NoteStore(Path.Combine(state, "vault"), Path.Combine(state, "backups")); store.Initialize();
        return new MainWindow(store, new Settings { Vault = store.Vault, Width = 800, Height = 400, Transparency = 45, Pinned = pinned, DesignVersion = 2 }, state);
    }
    static MenuItem PinItem(MainWindow window) {
        return ((ContextMenu)Call(window, "MoreMenu")).Items.OfType<MenuItem>().First(x => x.Header as string == "Always on top");
    }
    static void CheckSavedPin(string state, bool expected, string label) {
        Check(Settings.Load(state).Pinned == expected, label);
    }
    static void CheckLayerTransitions(string output, bool pinned) {
        string state = Path.Combine(output, pinned ? "pinned-state" : "normal-state");
        var window = NewWindow(state, pinned);
        new WindowInteropHelper(window).EnsureHandle();
        Check(!window.IsVisible && window.Topmost == pinned, "Hidden panel starts with saved Pinned=" + pinned);
        Call(window, "ToggleCollapsed"); Call(window, "StopDockAnimation");
        Check(!window.IsVisible && window.Topmost, "Bubble is topmost independently of Pinned=" + pinned);
        CheckSavedPin(state, pinned, "Collapse retains saved panel layer " + pinned);
        Check(PinItem(window).IsChecked == pinned, "Pin menu reports the panel preference while collapsed " + pinned);
        var pin = PinItem(window); pin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(window.Topmost && PinItem(window).IsChecked != pinned, "Changing the panel preference keeps the bubble topmost " + pinned);
        CheckSavedPin(state, !pinned, "Explicit pin change is persisted " + !pinned);
        PinItem(window).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Call(window, "ToggleCollapsed");
        Check(window.Topmost == pinned, "Expansion restores the selected panel layer " + pinned);
        Call(window, "ToggleCollapsed"); Call(window, "StopDockAnimation");
        window.HidePanel();
        CheckSavedPin(state, pinned, "Closing the bubble preserves panel preference " + pinned);
        window.Exit();
        CheckSavedPin(state, pinned, "Quit while collapsed preserves panel preference " + pinned);
        var restarted = new MainWindow(window.Store, Settings.Load(state), state);
        Check(restarted.Topmost == pinned && !Field<bool>(restarted, "collapsed"), "Restart opens the expanded panel at its saved layer " + pinned);
        restarted.Exit();
    }
    static void CheckHeader(string output) {
        var window = NewWindow(Path.Combine(output, "visual-state"), false);
        var buttons = new[] { Field<Button>(window, "more"), Field<Button>(window, "collapse"), Field<Button>(window, "close") };
        var names = new[] { "More", "Collapse / Expand", "Close panel" };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 96, Height = 30 };
        for (int i = 0; i < buttons.Length; i++) {
            var button = buttons[i]; ((Panel)button.Parent).Children.Remove(button); panel.Children.Add(button);
            Check(AutomationProperties.GetName(button) == names[i], names[i] + " retains its accessible action name");
        }
        var root = new Grid { Width = 96, Height = 30, Resources = Theme.Resources(), UseLayoutRounding = true, SnapsToDevicePixels = true };
        root.Children.Add(panel);
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) {
            VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
            root.Measure(new Size(96, 30)); root.Arrange(new Rect(0, 0, 96, 30)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Round(96 * scale), (int)Math.Round(30 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, "header-" + (100 * scale) + ".png"))) encoder.Save(file);
            int stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
            double[] centerY = new double[3];
            for (int i = 0; i < 3; i++) {
                double total = 0, sumX = 0, sumY = 0; int minX = int.MaxValue, maxX = -1;
                for (int y = 0; y < bitmap.PixelHeight; y++) for (int x = (int)(i * 32 * scale); x < (i + 1) * 32 * scale; x++) {
                    byte alpha = pixels[y * stride + x * 4 + 3];
                    if (alpha == 0) continue;
                    total += alpha; sumX += (x + .5) * alpha; sumY += (y + .5) * alpha; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                }
                Check(total > 0 && Math.Abs(sumX / total - (i * 32 + 16) * scale) <= .51 && Math.Abs(sumY / total - 15 * scale) <= .51,
                    (100 * scale) + "% " + names[i] + " glyph is centered to half a physical pixel");
                centerY[i] = sumY / total;
                if (i == 1) Check((maxX - minX + 1) / scale <= 12.5, (100 * scale) + "% collapse line remains short");
            }
            Check(centerY.Max() - centerY.Min() <= .51, (100 * scale) + "% ellipsis, collapse and close share a visual centerline");
        }
        window.Exit();
    }
    [STAThread] static void Main(string[] args) {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); Theme.SetDark(true);
        CheckHeader(output); CheckLayerTransitions(output, false); CheckLayerTransitions(output, true);
        app.Shutdown(); Console.WriteLine(checks + " header and panel-layer checks passed without showing a window.");
    }
}
