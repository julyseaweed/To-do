using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shike;

// Calls the same resize lifecycle as WM_SIZING, with a detached visual root.
// All settings and notes are isolated; no HWND or global input is created.
class CornerScalingTests {
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static MainWindow window;
    static FrameworkElement root;
    static NoteStore store;
    static Settings settings;
    static string output, state, fixture;
    static double dpi;
    static int checks;
    static readonly List<string> report = new List<string> { "case,element,before_x,before_y,before_width,before_height,after_x,after_y,after_width,after_height,scale" };
    static object Field(string name) { return typeof(MainWindow).GetField(name, Private).GetValue(window); }
    static void Set(string name, object value) { typeof(MainWindow).GetField(name, Private).SetValue(window, value); }
    static object Call(string name, params object[] values) { return typeof(MainWindow).GetMethod(name, Private).Invoke(window, values); }
    static double Scale { get { return (double)typeof(Settings).GetField("InterfaceScale").GetValue(settings); } set { typeof(Settings).GetField("InterfaceScale").SetValue(settings, value); } }
    static Size NativeSize(Size size) { return new Size(Math.Round(size.Width * dpi) / dpi, Math.Round(size.Height * dpi) / dpi); }
    static Size ActualSize { get { return new Size(root.ActualWidth, root.ActualHeight); } }
    static string N(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    static string ScaleNumber(double value) { return value.ToString("0.#########", CultureInfo.InvariantCulture); }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject element) {
        yield return element;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(element, index))) yield return child;
    }
    static T Named<T>(string name) where T : DependencyObject { return Descendants(root).OfType<T>().First(x => AutomationProperties.GetName(x) == name); }
    static Rect Bounds(FrameworkElement element) { return element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
    static Border Card(TextBox title) {
        for (DependencyObject current = title; current != null; current = VisualTreeHelper.GetParent(current)) {
            var border = current as Border;
            if (border != null && border.Background is LinearGradientBrush) return border;
        }
        throw new Exception("Card not found.");
    }
    static Size Layout(Size size, bool edge = false) {
        size = NativeSize(size);
        window.Width = size.Width; window.Height = size.Height;
        if (edge) Call("UpdateScalingViewport", size);
        root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
        root.Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle);
        root.Measure(size); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
        return ActualSize;
    }
    static void Open(Size size, double scale, double textZoom, double screenDpi, string name) {
        state = Path.Combine(output, name); Directory.CreateDirectory(state);
        store = new NoteStore(Path.Combine(state, "vault"), Path.Combine(state, "backups")); store.Initialize();
        fixture = "# Corner scaling fixture\n\n## " + NoteStore.ReadingGroup + "\n";
        for (int row = 0; row < 16; row++) fixture += NoteDocument.Serialize(new Entry { Group = NoteStore.ReadingGroup, Title = "Reference " + row + " 关于", Link = "https://example.test/reading/" + row }, "\n") + "\n";
        for (int topic = 0; topic < 4; topic++) {
            string group = "Topic " + topic; fixture += "\n## " + group + "\n";
            for (int row = 0; row < 18; row++) fixture += NoteDocument.Serialize(new Entry { Group = group, Title = Title(topic, row), Note = row == 2 ? "Supporting note" : "" }, "\n") + "\n";
        }
        File.WriteAllText(store.CurrentPath, fixture, new System.Text.UTF8Encoding(false));
        settings = new Settings { Width = size.Width, Height = size.Height, Zoom = textZoom, WatchingWidth = 240, Pinned = false, DesignVersion = 2 };
        Scale = scale;
        window = new MainWindow(store, settings, state);
        root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
        root.SetValue(TextElement.FontFamilyProperty, window.FontFamily); root.SetValue(TextElement.FontSizeProperty, window.FontSize); root.SetValue(TextElement.ForegroundProperty, window.Foreground);
        root.UseLayoutRounding = true; root.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(root, TextOptions.GetTextFormattingMode(window));
        dpi = screenDpi; VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        foreach (string timer in new[] { "watcher", "contrastTimer" }) ((DispatcherTimer)Field(timer)).Stop();
        Layout(size, true);
    }
    static string Title(int topic, int row) { return "Plan " + topic + "." + row + (row == 2 ? " 关于 a longer task with several words for consistent wrapping" : " Project notes"); }
    sealed class Snapshot {
        public readonly Dictionary<string, Rect> Elements = new Dictionary<string, Rect>();
        public readonly Dictionary<string, double[]> Scrolls = new Dictionary<string, double[]>();
        public readonly Dictionary<string, int> VisibleRows = new Dictionary<string, int>();
        public Size Canvas; public Point Origin; public double RenderScale; public int Lines;
    }
    static Snapshot Capture() {
        var result = new Snapshot();
        var canvas = (FrameworkElement)Field("scalingCanvas"); result.Canvas = new Size(canvas.ActualWidth, canvas.ActualHeight);
        var transform = canvas.TransformToAncestor(root); result.Origin = transform.Transform(new Point());
        result.RenderScale = transform.Transform(new Point(1, 0)).X - result.Origin.X;
        foreach (string name in new[] { "More", "Collapse / Expand", "Close panel", "New topic", "Rename Topic 0", "Rename Watching list" }) result.Elements[name] = Bounds(Named<Button>(name));
        var title = (TextBox)Named<Button>("Edit " + Title(0, 2)).Content;
        result.Elements["title"] = Bounds(title); result.Elements["card"] = Bounds(Card(title));
        result.Elements["first glyph"] = title.TransformToAncestor(root).TransformBounds(title.GetRectFromCharacterIndex(0));
        result.Lines = title.LineCount;
        var check = Named<CheckBox>("Complete " + Title(0, 2));
        result.Elements["circle"] = Bounds((FrameworkElement)check.Template.FindName("RingShape", check));
        var strips = ((Dictionary<string, ScrollViewer>)Field("columnScrolls")).ToDictionary(x => x.Key, x => x.Value);
        strips.Add("Topics strip", (ScrollViewer)Field("scroll"));
        foreach (var pair in strips) {
            var scroll = pair.Value;
            result.Scrolls[pair.Key] = new[] { scroll.ViewportWidth, scroll.ViewportHeight, scroll.HorizontalOffset, scroll.VerticalOffset, scroll.ExtentWidth, scroll.ExtentHeight };
            var rows = scroll.Content as StackPanel;
            if (rows != null && pair.Key != "Topics strip") {
                Rect viewport = Bounds(scroll);
                result.VisibleRows[pair.Key] = rows.Children.OfType<FrameworkElement>().Count(x => Bounds(x).IntersectsWith(viewport));
            }
        }
        return result;
    }
    static void SaveImage(string name) {
        int width = (int)Math.Ceiling(root.ActualWidth * dpi), height = (int)Math.Ceiling(root.ActualHeight * dpi);
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
    }
    static void Compare(Snapshot before, Snapshot after, double ratio, string name) {
        Console.WriteLine("MEASURE " + name + ": canvas " + N(before.Canvas.Width) + "x" + N(before.Canvas.Height) + " -> " + N(after.Canvas.Width) + "x" + N(after.Canvas.Height) + ", configured scale " + N(Scale));
        double actualRatio = after.RenderScale / before.RenderScale;
        Check(Math.Abs(actualRatio - ratio) <= 2 / (Math.Min(before.Canvas.Width, before.Canvas.Height) * dpi), name + ": accepted native dimensions retain the intended scale within pixel rounding");
        ratio = actualRatio;
        Check(Math.Abs(before.Canvas.Width - after.Canvas.Width) < .01 && Math.Abs(before.Canvas.Height - after.Canvas.Height) < .01, name + ": virtual canvas dimensions stay fixed");
        Check(before.Lines == after.Lines, name + ": wrapped title keeps its line count");
        foreach (var pair in before.Elements) {
            Rect old = pair.Value, current = after.Elements[pair.Key];
            var expected = new Rect(after.Origin.X + (old.Left - before.Origin.X) * ratio, after.Origin.Y + (old.Top - before.Origin.Y) * ratio, old.Width * ratio, old.Height * ratio);
            double error = new[] { Math.Abs(current.Left - expected.Left), Math.Abs(current.Top - expected.Top), Math.Abs(current.Width - expected.Width), Math.Abs(current.Height - expected.Height) }.Max();
            Check(error * dpi <= 1.1, name + ": " + pair.Key + " scales uniformly (max " + N(error) + " DIP)");
            report.Add(string.Join(",", new[] { name, pair.Key, N(old.Left), N(old.Top), N(old.Width), N(old.Height), N(current.Left), N(current.Top), N(current.Width), N(current.Height), N(ratio) }));
        }
        foreach (var pair in before.Scrolls) Check(pair.Value.Zip(after.Scrolls[pair.Key], (left, right) => Math.Abs(left - right)).All(x => x < .01), name + ": " + pair.Key + " viewport, extent and offsets stay unchanged");
        Check(before.VisibleRows.All(x => after.VisibleRows[x.Key] == x.Value), name + ": the same rows remain visible");
        Check(Math.Abs(Bounds(Named<Button>("New topic")).Left - Bounds((ScrollViewer)Field("scroll")).Left) * dpi <= 1.1, name + ": footer stays aligned with topics pane");
    }
    static void Run(double screenDpi, double textZoom) {
        string name = "dpi" + N(screenDpi * 100) + "-zoom" + N(textZoom);
        var original = new Size(1000, 620); Open(original, 1, textZoom, screenDpi, name); original = ActualSize;
        var host = (Viewbox)Field("scalingHost"); var canvas = (FrameworkElement)host.Child;
        Check(host.Stretch == Stretch.Uniform && host.StretchDirection == StretchDirection.Both && !double.IsNaN(canvas.Width) && !double.IsNaN(canvas.Height), name + ": scaling uses a finite uniform canvas");
        ((ScrollViewer)Field("scroll")).ScrollToHorizontalOffset(42);
        foreach (var list in ((Dictionary<string, ScrollViewer>)Field("columnScrolls")).Values) list.ScrollToVerticalOffset(56);
        Layout(original);
        var before = Capture(); if (screenDpi == 2 && textZoom == 1) SaveImage(name + "-before");
        Check((bool)Call("BeginCornerScaling", original), name + ": corner scaling begins");
        var proposed = new Size(2 + (original.Width - 2) * .8, 2 + (original.Height - 2) * .8);
        var shrunk = Layout((Size)Call("ResizeCornerScaling", NativeSize(proposed)));
        Check(Math.Abs(Scale - .8) <= 2 / (Math.Min(before.Canvas.Width, before.Canvas.Height) * dpi), name + ": corner shrink updates only the outer scale within native pixel rounding");
        Compare(before, Capture(), .8, name + " shrink");
        if (screenDpi == 2 && textZoom == 1) SaveImage(name + "-shrunk");
        Call("EndCornerScaling", shrunk); Layout(shrunk);
        double savedScale = Scale;
        Check(Math.Abs(savedScale - Capture().RenderScale) < .00001, name + ": released setting equals the final rendered scale (saved " + ScaleNumber(savedScale) + ", rendered " + ScaleNumber(Capture().RenderScale) + ")");
        Compare(before, Capture(), .8, name + " released");
        Check(settings.Zoom == textZoom && File.ReadAllText(store.CurrentPath) == fixture, name + ": text zoom and notes stay unchanged");
        var stored = Settings.Load(state);
        Check(Math.Abs((double)typeof(Settings).GetField("InterfaceScale").GetValue(stored) - savedScale) < .00001, name + ": released scale persists");
        Check((bool)Call("BeginCornerScaling", shrunk), name + ": next corner gesture begins from its current size");
        var abandoned = Layout((Size)Call("ResizeCornerScaling", NativeSize(new Size(shrunk.Width * 1.2, shrunk.Height * 1.2))));
        Layout(shrunk); Call("EndCornerScaling", shrunk); Layout(shrunk);
        Check(Math.Abs(Scale - savedScale) < .00001, name + ": cancelled native size restores the prior scale (saved " + ScaleNumber(savedScale) + ", actual " + ScaleNumber(Scale) + ", rendered " + ScaleNumber(Capture().RenderScale) + ")");
        Compare(before, Capture(), .8, name + " cancelled");
        var edge = Layout(new Size(shrunk.Width + 120, shrunk.Height + 65), true);
        Check(Math.Abs(Scale - savedScale) < .00001 && canvas.Width > before.Canvas.Width && canvas.Height > before.Canvas.Height, name + ": edge resizing changes viewport while retaining scale");
        var edgeSnapshot = Capture();
        Console.WriteLine("MEASURE before next corner: requested outer " + N(edge.Width) + "x" + N(edge.Height) + ", arranged root " + N(root.ActualWidth) + "x" + N(root.ActualHeight) + ", canvas requested " + N(canvas.Width) + "x" + N(canvas.Height) + ", actual " + N(canvas.ActualWidth) + "x" + N(canvas.ActualHeight));
        double controlExpected = before.Elements["More"].Width * savedScale, controlActual = edgeSnapshot.Elements["More"].Width;
        Check(Math.Abs(controlActual - controlExpected) * dpi <= .51, name + ": edge resizing keeps control sizes (expected " + N(controlExpected) + ", actual " + N(controlActual) + " DIP)");
        Check((bool)Call("BeginCornerScaling", edge), name + ": corner scaling also works after an edge resize");
        Console.WriteLine("MEASURE after corner begin: canvas requested " + N(canvas.Width) + "x" + N(canvas.Height));
        var restored = Layout((Size)Call("ResizeCornerScaling", NativeSize(new Size(2 + (edge.Width - 2) / savedScale, 2 + (edge.Height - 2) / savedScale)))); Call("EndCornerScaling", restored); Layout(restored);
        Compare(edgeSnapshot, Capture(), Scale / savedScale, name + " enlarged");
        Check(Math.Abs(Scale - 1) <= 2 / (Math.Min(edgeSnapshot.Canvas.Width, edgeSnapshot.Canvas.Height) * dpi), name + ": enlarging restores original control scale within native pixel rounding");
        window.Exit(); window = null;
    }
    static void BoundsAndBubble() {
        var original = new Size(1000, 620); Open(original, .85, 1, 2, "bounds-bubble");
        Check((bool)Call("BeginCornerScaling", original), "Bounds: corner gesture begins");
        var minimum = (Size)Call("ResizeCornerScaling", new Size(1, 1)); Layout(minimum);
        Check(Scale >= .65 && Scale <= 1.8 && minimum.Width >= window.MinWidth && minimum.Height >= window.MinHeight, "Extreme shrink respects interface and native minimum sizes");
        var maximum = (Size)Call("ResizeCornerScaling", new Size(5000, 5000)); Layout(maximum);
        Check(Math.Abs(Scale - 1.8) < .00001 && Math.Abs((maximum.Width - 2) / (maximum.Height - 2) - (original.Width - 2) / (original.Height - 2)) < .00001, "Extreme enlargement preserves aspect at maximum scale");
        Call("EndCornerScaling", maximum); Layout(maximum);
        Check((window.MinWidth - 2) / Scale >= 338 - .01 && (window.MinHeight - 2) / Scale >= 278 - .01, "Enlarged interface reserves a usable virtual viewport during edge resizing");
        Check((bool)Call("BeginCornerScaling", maximum) && window.MinWidth == 340 && window.MinHeight == 280, "A new corner gesture can shrink an enlarged interface again");
        Layout(original); Call("EndCornerScaling", original); Layout(original);
        var before = Capture();
        Set("collapsed", true); ((UIElement)Field("surface")).Visibility = Visibility.Collapsed;
        var bubble = (Grid)Field("bubble"); bubble.Visibility = Visibility.Visible;
        window.MinWidth = window.MinHeight = 64; Layout(new Size(64, 64));
        Check(bubble.ActualWidth == 64 && bubble.ActualHeight == 64 && !((Visual)Field("scalingHost")).IsAncestorOf(bubble), "Collapsed bubble remains a separate unscaled 64-DIP visual");
        Check(!(bool)Call("BeginCornerScaling", new Size(64, 64)) && Math.Abs(Scale - .85) < .00001, "Collapsed bubble cannot change the expanded interface scale");
        Set("collapsed", false); bubble.Visibility = Visibility.Collapsed; ((UIElement)Field("surface")).Visibility = Visibility.Visible;
        window.MinWidth = 340; window.MinHeight = 280; Layout(original, true);
        Compare(before, Capture(), 1, "expanded visual restoration");
        Call("SaveSettings"); var saved = Settings.Load(state); window.Exit(); window = null;
        window = new MainWindow(store, saved, state); settings = saved;
        root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
        VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        Layout(original, true);
        Check(Math.Abs(Scale - .85) < .00001 && File.ReadAllText(store.CurrentPath) == fixture, "Reopening preserves the non-default .85 expanded scale and all fixture notes");
        Check(Math.Abs(Bounds(Named<Button>("More")).Width - before.Elements["More"].Width) * dpi <= .51, "Reopened controls use the persisted non-default scale");
        window.Exit(); window = null;
    }
    static Rect[] InlineGeometry(TextBox field, TextBox title, CheckBox check) {
        Rect fieldBounds = Bounds(field), glyph = field.TransformToAncestor(root).TransformBounds(field.GetRectFromCharacterIndex(0));
        Rect circle = Bounds((FrameworkElement)check.Template.FindName("RingShape", check));
        Rect titleCaret = title.TransformToAncestor(root).TransformBounds(title.GetRectFromCharacterIndex(0));
        Check(Math.Abs(circle.Top + circle.Height / 2 - titleCaret.Top - titleCaret.Height / 2) * dpi <= .8, "Scaled completion circle stays aligned with the first title line");
        return new[] { fieldBounds, glyph, Bounds(Card(field)), circle };
    }
    static void InlineScaleChecks() {
        foreach (double scale in new[] { .65, .8 }) {
            var size = new Size(900, 600); Open(size, scale, 1, 2, "inline-scale" + N(scale));
            foreach (bool link in new[] { false, true }) {
                string title = link ? "Reference 0 关于" : Title(0, 2);
                var entry = ((NoteDocument)Field("document")).Entries.First(x => x.Title == title);
                var titleField = (TextBox)Named<Button>("Edit " + title).Content;
                var display = link ? (TextBox)Named<Button>("Edit link " + title).Content : titleField;
                var before = InlineGeometry(display, titleField, Named<CheckBox>("Complete " + title));
                if (link) Call("EditLink", entry); else window.Edit(entry, entry.Group);
                Layout(size);
                object draft = Field("inline"); var draftType = draft.GetType();
                var editing = (TextBox)draftType.GetField(link ? "LinkInput" : "Input").GetValue(draft);
                var editingTitle = (TextBox)draftType.GetField("Input").GetValue(draft);
                var during = InlineGeometry(editing, editingTitle, Named<CheckBox>("Complete current item"));
                Check(before.Zip(during, (a, b) => Math.Max(Math.Max(Math.Abs(a.Left - b.Left), Math.Abs(a.Top - b.Top)), Math.Max(Math.Abs(a.Width - b.Width), Math.Abs(a.Height - b.Height)))).All(x => x * dpi <= .8), "Scale " + N(scale) + (link ? " Watching link" : " task title") + " enters editing without glyph/card/circle movement");
                Check((bool)Call("FinishInline", false), "Unchanged scaled editor commits successfully"); Layout(size);
                titleField = (TextBox)Named<Button>("Edit " + title).Content;
                display = link ? (TextBox)Named<Button>("Edit link " + title).Content : titleField;
                var after = InlineGeometry(display, titleField, Named<CheckBox>("Complete " + title));
                Check(before.Zip(after, (a, b) => Math.Max(Math.Max(Math.Abs(a.Left - b.Left), Math.Abs(a.Top - b.Top)), Math.Max(Math.Abs(a.Width - b.Width), Math.Abs(a.Height - b.Height)))).All(x => x * dpi <= .8), "Scale " + N(scale) + (link ? " Watching link" : " task title") + " leaves editing without reflow");
            }
            Check(File.ReadAllText(store.CurrentPath) == fixture, "Scaled no-op edits preserve fixture bytes");
            window.Exit(); window = null;
        }
    }
    [STAThread] static int Main(string[] args) {
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            if (args.Length > 1) Run(double.Parse(args[1], CultureInfo.InvariantCulture), 1);
            else {
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Run(scale, 1);
                Run(2, .8); Run(2, 1.5); BoundsAndBubble(); InlineScaleChecks();
            }
            Console.WriteLine(checks + " corner scaling checks passed without showing a window."); return 0;
        } catch (Exception ex) { for (; ex != null; ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1; }
        finally { File.WriteAllLines(Path.Combine(output, "metrics.csv"), report); if (window != null) { Set("inline", null); window.Exit(); } app.Shutdown(); }
    }
}
