using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shike;

// Offscreen visual regression check: this never shows or activates a window.
class RenderCloseButtons {
    static int checks;
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static T Field<T>(MainWindow window, string name) {
        return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
    }
    static RenderTargetBitmap Render(FrameworkElement root, double width, double height, double scale) {
        VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root); return bitmap;
    }
    static void Save(BitmapSource bitmap, string path) {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(path)) encoder.Save(file);
    }
    static void SetReadOnly(Button button, string name, bool value) {
        for (Type type = button.GetType(); type != null; type = type.BaseType) {
            var field = type.GetField(name + "PropertyKey", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) { button.SetValue((DependencyPropertyKey)field.GetValue(null), value); return; }
        }
        throw new Exception("Missing visual-state property: " + name);
    }
    static void CheckGlyph(Button button, FrameworkElement root, Point expected, double scale, string label) {
        button.ApplyTemplate();
        var glyph = (System.Windows.Shapes.Path)button.Template.FindName("CloseGlyph", button);
        Rect bounds = glyph.RenderedGeometry.GetRenderBounds(new Pen(glyph.Stroke, glyph.StrokeThickness));
        Point center = glyph.TransformToAncestor(root).Transform(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        Check(Math.Abs(center.X - expected.X) * scale <= 0.51 && Math.Abs(center.Y - expected.Y) * scale <= 0.51,
            label + " vector center within half a physical pixel: " + center);
    }
    static void CheckPixels(RenderTargetBitmap bitmap, double scale) {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        byte[] pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        double total = 0, xMoment = 0, yMoment = 0;
        int outsideAlpha = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            int i = (y * width + x) * 4;
            double dipX = (x + .5) / scale, dipY = (y + .5) / scale;
            if (Math.Pow(dipX - 54, 2) + Math.Pow(dipY - 10, 2) < 36) {
                double weight = Math.Max(0, Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) - 140);
                total += weight; xMoment += (x + .5) * weight; yMoment += (y + .5) * weight;
            }
            bool outsideMain = Math.Pow(dipX - 28, 2) + Math.Pow(dipY - 36, 2) > Math.Pow(28 + 1 / scale, 2);
            bool outsideClose = Math.Pow(dipX - 54, 2) + Math.Pow(dipY - 10, 2) > Math.Pow(10 + 1 / scale, 2);
            if (outsideMain && outsideClose) outsideAlpha = Math.Max(outsideAlpha, pixels[i + 3]);
        }
        Check(total > 0 && Math.Abs(xMoment / total - 54 * scale) <= 0.51 && Math.Abs(yMoment / total - 10 * scale) <= 0.51,
            (scale * 100) + "% rasterized X centered: (" + (xMoment / total).ToString("F3") + ", " + (yMoment / total).ToString("F3") + ")");
        Check(outsideAlpha == 0, (scale * 100) + "% bubble has no painted rectangular background");
    }
    [STAThread] static void Main(string[] args) {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        string state = Path.Combine(output, "isolated-state");
        var store = new NoteStore(state, Path.Combine(state, "backups")); store.Initialize();
        Theme.SetDark(true);
        var window = new MainWindow(store, new Settings { Width = 800, Height = 400, Transparency = 45, Pinned = false }, state);
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.Resources = Theme.Resources(); root.UseLayoutRounding = true; root.SnapsToDevicePixels = true;
        Field<Border>(window, "surface").Visibility = Visibility.Collapsed;
        Grid bubble = Field<Grid>(window, "bubble"); bubble.Visibility = Visibility.Visible;
        var dismiss = (Button)bubble.Children[1];
        Check(System.Windows.Automation.AutomationProperties.GetName(dismiss) == "Close bubble", "Close bubble automation name is preserved");
        Check(dismiss.Width == 20 && dismiss.Height == 20, "Close bubble keeps the 20-DIP circular hit target");
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) {
            var bitmap = Render(root, 64, 64, scale);
            Save(bitmap, Path.Combine(output, "bubble-" + (scale * 100) + ".png"));
            CheckGlyph(dismiss, root, new Point(54, 10), scale, (scale * 100) + "% collapsed");
            CheckPixels(bitmap, scale);
        }
        var feedback = (System.Windows.Shapes.Ellipse)dismiss.Template.FindName("Feedback", dismiss);
        SetReadOnly(dismiss, "IsMouseOver", true);
        Save(Render(root, 64, 64, 2), Path.Combine(output, "bubble-hover.png"));
        Check(feedback.Fill == Theme.Hover, "Hover feedback preserves the round surface");
        CheckGlyph(dismiss, root, new Point(54, 10), 2, "Hover");
        SetReadOnly(dismiss, "IsPressed", true);
        Save(Render(root, 64, 64, 2), Path.Combine(output, "bubble-pressed.png"));
        Check(feedback.Fill == Theme.Pressed, "Pressed feedback has priority over hover");
        CheckGlyph(dismiss, root, new Point(54, 10), 2, "Pressed");
        SetReadOnly(dismiss, "IsPressed", false); SetReadOnly(dismiss, "IsMouseOver", false);
        var unfocused = Render(root, 64, 64, 2); byte[] beforeFocus = new byte[unfocused.PixelWidth * unfocused.PixelHeight * 4]; unfocused.CopyPixels(beforeFocus, unfocused.PixelWidth * 4, 0);
        SetReadOnly(dismiss, "IsKeyboardFocused", true);
        var focused = Render(root, 64, 64, 2); byte[] afterFocus = new byte[beforeFocus.Length]; focused.CopyPixels(afterFocus, focused.PixelWidth * 4, 0);
        Save(focused, Path.Combine(output, "bubble-mouse-focus.png"));
        Check(dismiss.FocusVisualStyle != null && dismiss.Template.FindName("FocusRing", dismiss) == null && System.Linq.Enumerable.SequenceEqual(beforeFocus, afterFocus), "Ordinary focus does not add a mouse-click ring; keyboard focus has a separate cue");
        var cue = new Control { Style = dismiss.FocusVisualStyle, Width = 20, Height = 20 };
        var cueRoot = new Grid { Width = 20, Height = 20, Resources = Theme.Resources() }; cueRoot.Children.Add(cue);
        var cueBitmap = Render(cueRoot, 20, 20, 2); Save(cueBitmap, Path.Combine(output, "bubble-keyboard-focus-cue.png"));
        var cueEllipse = VisualTreeHelper.GetChild(cue, 0) as System.Windows.Shapes.Ellipse;
        Check(cueEllipse != null && cueEllipse.Margin == new Thickness(2) && cueEllipse.Stroke != null, "Keyboard focus cue remains inset inside the circular button");
        CheckGlyph(dismiss, root, new Point(54, 10), 2, "Keyboard focus");
        SetReadOnly(dismiss, "IsKeyboardFocused", false);
        var close = Field<Button>(window, "close");
        ((Panel)close.Parent).Children.Remove(close);
        var closeRoot = new Grid { Width = 32, Height = 30, Resources = Theme.Resources(), UseLayoutRounding = true };
        closeRoot.Children.Add(close);
        Check(System.Windows.Automation.AutomationProperties.GetName(close) == "Close panel", "Close panel automation name is preserved");
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) {
            var bitmap = Render(closeRoot, 32, 30, scale);
            Save(bitmap, Path.Combine(output, "close-" + (scale * 100) + ".png"));
            CheckGlyph(close, closeRoot, new Point(16, 15), scale, (scale * 100) + "% expanded");
        }
        window.Exit(); app.Shutdown();
        Console.WriteLine(checks + " offscreen visual checks passed.");
    }
}
