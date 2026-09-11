using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Shike;

// Detached WPF controls only: no HWND, tray icon, input, or visible menu.
class CompletionStyleTests {
    static int checks;
    static string output;
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static bool Clear(Brush brush) {
        var solid = brush as SolidColorBrush;
        return brush == null || brush.Opacity == 0 || solid != null && solid.Color.A <= 1;
    }
    static void SetReadOnly(DependencyObject value, string name, bool enabled) {
        for (Type type = value.GetType(); type != null; type = type.BaseType) {
            var key = type.GetField(name + "PropertyKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (key != null) { value.SetValue((DependencyPropertyKey)key.GetValue(null), enabled); return; }
        }
        throw new Exception("Missing WPF state: " + name);
    }
    static Rect ShapeBounds(Shape shape, Visual root) {
        var bounds = shape.RenderedGeometry.Bounds;
        return shape.TransformToAncestor(root).TransformBounds(bounds);
    }
    static Point Center(Rect r) { return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2); }
    static void Save(BitmapSource bitmap, string name) {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(System.IO.Path.Combine(output, name + ".png"))) encoder.Save(file);
    }
    static RenderTargetBitmap Render(FrameworkElement root, double scale) {
        VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
        root.Measure(new Size(30, 34)); root.Arrange(new Rect(0, 0, 30, 34)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Round(30 * scale), (int)Math.Round(34 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root); return bitmap;
    }
    static void CheckPixels(RenderTargetBitmap bitmap, Rect ringBounds, double ringStroke, Rect dotBounds, Color ink, bool selected, double scale, string label) {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Point ringCenter = Center(ringBounds), dotCenter = Center(dotBounds);
        double inner = ringBounds.Width / 2 - ringStroke / 2, radius = dotBounds.Width / 2;
        int clearPixels = 0, gapPixels = 0, maxGapAlpha = 0; var clearQuadrants = new bool[4]; double total = 0, sumX = 0, sumY = 0;
        for (int y = 0; y < bitmap.PixelHeight; y++) for (int x = 0; x < bitmap.PixelWidth; x++) {
            double dx = (x + .5) / scale - dotCenter.X, dy = (y + .5) / scale - dotCenter.Y;
            double fromDot = Math.Sqrt(dx * dx + dy * dy);
            dx = (x + .5) / scale - ringCenter.X; dy = (y + .5) / scale - ringCenter.Y;
            double fromRing = Math.Sqrt(dx * dx + dy * dy);
            byte alpha = pixels[(y * bitmap.PixelWidth + x) * 4 + 3];
            // A diagonal edge can cross a pixel whose center is only half a
            // pixel away. Require full transparency only for wholly interior
            // pixels, while checking a clear gap in every quadrant at all DPIs.
            if (fromDot > radius + .75 / scale && fromRing < inner - .75 / scale) { gapPixels++; maxGapAlpha = Math.Max(maxGapAlpha, alpha); if (alpha <= 12) clearPixels++; }
            if (fromDot > radius + .2 / scale && fromRing < inner - .2 / scale && alpha <= 12)
                clearQuadrants[(dx < 0 ? 0 : 1) + (dy < 0 ? 0 : 2)] = true;
            if (fromDot < radius + .4 / scale && fromRing < inner - .5 / scale) { total += alpha; sumX += (x + .5) * alpha; sumY += (y + .5) * alpha; }
        }
        Check(clearQuadrants.All(x => x) && clearPixels == gapPixels, label + " has a transparent pixel gap between dot and ring (" + clearPixels + "/" + gapPixels + " interior pixels, alpha " + maxGapAlpha + ")");
        if (selected) {
            Check(total > 0 && Math.Abs(sumX / total - dotCenter.X * scale) <= .51 && Math.Abs(sumY / total - dotCenter.Y * scale) <= .51, label + " filled dot is raster-centered");
            int offset = ((int)(dotCenter.Y * scale) * bitmap.PixelWidth + (int)(dotCenter.X * scale)) * 4;
            Check(pixels[offset + 3] >= 250 && Math.Abs(pixels[offset] - ink.B) <= 2 && Math.Abs(pixels[offset + 1] - ink.G) <= 2 && Math.Abs(pixels[offset + 2] - ink.R) <= 2, label + " dot uses the card's contrasting ink");
        } else Check(total <= 1, label + " unchecked center stays empty");
    }
    static void CheckStyles() {
        foreach (int colorIndex in new[] { 0, 1 }) {
            string theme = colorIndex == 0 ? "burgundy" : "bright";
            var ink = (SolidColorBrush)Theme.CardForeground(colorIndex);
            var root = new Grid { Width = 30, Height = 34, Resources = Theme.Resources(), UseLayoutRounding = true, SnapsToDevicePixels = true };
            root.Resources["Ink"] = ink; root.Resources["Ring"] = Theme.CardSecondary(colorIndex); root.Resources["Focus"] = ink;
            var check = new CheckBox(); root.Children.Add(check);
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) {
                foreach (bool selected in new[] { false, true }) foreach (int state in new[] { 0, 1, 2, 3 }) {
                    check.IsChecked = selected; SetReadOnly(check, "IsMouseOver", (state & 1) != 0); SetReadOnly(check, "IsKeyboardFocused", (state & 2) != 0);
                    var bitmap = Render(root, scale); check.ApplyTemplate();
                    var ring = check.Template.FindName("RingShape", check) as Ellipse;
                    var dot = check.Template.FindName("CompletionDot", check) as Ellipse;
                    string label = theme + " " + (100 * scale) + "% " + (selected ? "checked" : "empty") + " state" + state;
                    Check(ring != null && dot != null && check.Template.FindName("Tick", check) == null, label + " uses a ring and a circular dot, without a tick");
                    // Hidden shapes have no RenderedGeometry; temporarily measure
                    // their exact same visible state for bounds, then render empty.
                    if (!selected) { check.IsChecked = true; Render(root, scale); }
                    Rect ringBounds = ShapeBounds(ring, root), dotBounds = ShapeBounds(dot, root);
                    double centers = (Center(ringBounds) - Center(dotBounds)).Length;
                    Check(centers * scale <= .72 && ringBounds.Width / 2 - ring.StrokeThickness / 2 - dotBounds.Width / 2 - centers >= 1.0, label + " retains a centered dot and visible annular spacing");
                    if (!selected) { check.IsChecked = false; bitmap = Render(root, scale); }
                    Check(Clear(ring.Fill), label + " outer ring never fills on check, hover or focus");
                    Save(bitmap, theme + "-" + (100 * scale) + "-" + (selected ? "checked" : "empty") + "-" + state);
                    CheckPixels(bitmap, ringBounds, ring.StrokeThickness, dotBounds, ink.Color, selected, scale, label);
                }
            }
        }
    }
    static void CheckMenu() {
        string state = System.IO.Path.Combine(output, "isolated-state");
        var store = new NoteStore(System.IO.Path.Combine(state, "vault"), System.IO.Path.Combine(state, "backups")); store.Initialize();
        var window = new MainWindow(store, new Settings { Width = 800, Height = 400, Pinned = false }, state);
        try {
            foreach (bool resident in new[] { false, true }) {
                typeof(MainWindow).GetField("resident", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, resident);
                var menu = (ContextMenu)typeof(MainWindow).GetMethod("MoreMenu", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                var labels = menu.Items.OfType<MenuItem>().Select(x => x.Header as string).Where(x => x != null).ToArray();
                var expected = resident ? new[] { "Always on top", "Open at login" } : new[] { "Always on top" };
                Check(labels.SequenceEqual(expected), (resident ? "Resident" : "Normal") + " More menu contains only the requested actions");
                var sliders = menu.Items.OfType<MenuItem>().Select(x => x.Header).OfType<StackPanel>().SelectMany(x => x.Children.OfType<Slider>()).ToArray();
                Check(sliders.Length == 1 && sliders[0].Minimum == 35 && sliders[0].Maximum == 95 && System.Windows.Automation.AutomationProperties.GetName(sliders[0]) == "Glass transparency", "Transparency uses the requested 35–95% range");
                Check(!((MenuItem)menu.Items[0]).Focusable && sliders[0].Focusable, "The transparency slider owns focus instead of its menu-row wrapper");
                var items = menu.Items.Cast<object>().ToArray();
                Check(!(items.First() is Separator) && !(items.Last() is Separator) && !items.Skip(1).Where((x, i) => x is Separator && items[i] is Separator).Any(), "More menu has no leading, trailing or duplicate separators");
                if (resident) CheckMenuGeometry(menu, sliders[0]);
            }
        } finally { window.Exit(); }
    }
    static IEnumerable<DependencyObject> Children(DependencyObject value) {
        yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++) foreach (var child in Children(VisualTreeHelper.GetChild(value, i))) yield return child;
    }
    static Rect RelativeBounds(FrameworkElement element, Visual root) { return element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
    static void MenuLayout(FrameworkElement root, double dpi) {
        VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        // A detached reused tree has no HWND DPI-change message to invalidate
        // cached child measurements; invalidate it before comparing states.
        foreach (var child in Children(root).OfType<FrameworkElement>()) { child.InvalidateMeasure(); child.InvalidateArrange(); }
        root.Measure(new Size(320, 320)); root.Arrange(new Rect(0, 0, 320, 320)); root.UpdateLayout();
    }
    static void SaveMenu(FrameworkElement root, double dpi, string name) {
        var bitmap = new RenderTargetBitmap((int)(320 * dpi), (int)(320 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(root); Save(bitmap, name);
    }
    static void CheckMenuGeometry(ContextMenu menu, Slider slider) {
        // ContextMenu is laid out directly, without opening its Popup/HWND.
        FrameworkElement root = menu; menu.Resources = Theme.Resources(); menu.UseLayoutRounding = true;
        menu.Visibility = Visibility.Visible; menu.HorizontalAlignment = HorizontalAlignment.Left; menu.VerticalAlignment = VerticalAlignment.Top;
        var label = ((StackPanel)((MenuItem)menu.Items[0]).Header).Children.OfType<TextBlock>().Single();
        var pin = menu.Items.OfType<MenuItem>().Single(x => x.Header as string == "Always on top");
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) {
            slider.Value = 35; MenuLayout(root, scale);
            var track = slider.Template.FindName("PART_Track", slider) as Track;
            Check(track != null && track.Thumb != null && slider.FocusVisualStyle != null, (100 * scale) + "% menu slider has a real track/thumb and keyboard focus cue");
            var actionLabels = menu.Items.OfType<MenuItem>().Where(x => x.Header is string).Select(x => Children(x).OfType<ContentPresenter>().First(p => p.Content as string == x.Header as string)).ToArray();
            Rect labelRect = RelativeBounds(label, root), sliderRect = RelativeBounds(slider, root), menuRect = RelativeBounds(menu, root);
            var separatorLines = menu.Items.OfType<Separator>().Select(x => Children(x).OfType<Border>().Single()).Select(x => RelativeBounds(x, root)).ToArray();
            Check(separatorLines.All(x => Math.Abs(x.Left - labelRect.Left) * scale <= .51 && Math.Abs(x.Right - sliderRect.Right) * scale <= .51),
                (100 * scale) + "% menu separators align with the label and slider edges");
            var actionRects = actionLabels.Select(x => RelativeBounds(x, root)).ToArray();
            Check(actionRects.All(x => Math.Abs(x.Left - labelRect.Left) * scale <= .51) && Math.Abs(sliderRect.Left - labelRect.Left) * scale <= .51,
                (100 * scale) + "% transparency label, slider, and menu labels share one left edge without an icon gutter");
            double[] thumbCenters = new double[3]; int valueIndex = 0;
            foreach (double value in new[] { 35.0, 65.0, 95.0 }) {
                slider.Value = value; MenuLayout(root, scale);
                Rect thumb = RelativeBounds(track.Thumb, root); thumbCenters[valueIndex++] = thumb.Left + thumb.Width / 2;
                Check(label.Text == "Transparency  " + value.ToString("0") + "%" && RelativeBounds(label, root) == labelRect && RelativeBounds(slider, root) == sliderRect && RelativeBounds(menu, root) == menuRect,
                    (100 * scale) + "% transparency " + value + "% updates its label without moving menu controls");
                Check(Math.Abs(thumb.Top + thumb.Height / 2 - (sliderRect.Top + sliderRect.Height / 2)) * scale <= .51 && thumb.Left >= sliderRect.Left - .01 && thumb.Right <= sliderRect.Right + .01,
                    (100 * scale) + "% transparency " + value + "% thumb remains centered and unclipped");
            }
            Check(thumbCenters[0] < thumbCenters[1] && thumbCenters[1] < thumbCenters[2] && Math.Abs(thumbCenters[1] - (thumbCenters[0] + thumbCenters[2]) / 2) * scale <= .51,
                (100 * scale) + "% slider endpoints and midpoint follow the requested range");
            foreach (int state in new[] { 0, 1, 2, 3 }) {
                SetReadOnly(pin, "IsHighlighted", (state & 1) != 0); pin.IsChecked = (state & 2) != 0;
                SetReadOnly(slider, "IsMouseOver", (state & 1) != 0); SetReadOnly(slider, "IsKeyboardFocused", (state & 2) != 0);
                MenuLayout(root, scale);
                var nowActions = actionLabels.Select(x => RelativeBounds(x, root)).ToArray();
                bool stable = RelativeBounds(menu, root) == menuRect && RelativeBounds(slider, root) == sliderRect && nowActions.SequenceEqual(actionRects);
                if (!stable) Console.WriteLine("DIAG menu " + menuRect + " -> " + RelativeBounds(menu, root) + "; slider " + sliderRect + " -> " + RelativeBounds(slider, root) + "; labels " + string.Join(" | ", actionRects.Select(x => x.ToString())) + " -> " + string.Join(" | ", nowActions.Select(x => x.ToString())));
                SaveMenu(root, scale, "menu-" + (100 * scale) + "-state" + state);
                Check(stable,
                    (100 * scale) + "% menu hover/check/focus state " + state + " does not shift labels or controls");
            }
        }
        Check(!menu.IsOpen, "Menu visual QA never opens a native popup");
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
    }
    [STAThread] static int Main(string[] args) {
        output = System.IO.Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try { if (args.Length < 2 || args[1] != "menu") CheckStyles(); CheckMenu(); Console.WriteLine(checks + " " + (args.Length > 1 && args[1] == "menu" ? "menu" : "completion-style and minimal-menu") + " checks passed without showing a window."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1; }
        finally { app.Shutdown(); }
    }
}
