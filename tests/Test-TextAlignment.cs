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
using Shike;

// Paired display/edit renders and glyph geometry, with no window or input.
partial class TextAlignmentTests {
    sealed class Metrics {
        public Rect Control, Host;
        public List<Rect> Characters = new List<Rect>(), GlyphLines = new List<Rect>();
    }
    sealed class Sample { public string Name, Value; public Sample(string name, string value) { Name = name; Value = value; } }
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static MainWindow window; static FrameworkElement root; static NoteStore store;
    static string output; static double dpi; static int cases, failures;
    static double layoutWidth = 900, layoutHeight = 640;
    static string scenarioSuffix = "";
    static readonly List<string> report = new List<string> { "case,phase,control_dx,control_dy,control_dw,control_dh,host_dx,host_dy,host_dw,host_dh,glyph_dx,glyph_dy,display_lines,edit_lines,char_dx,char_dy" };
    static object Field(string name) { return typeof(MainWindow).GetField(name, Private).GetValue(window); }
    static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, Private).Invoke(window, args); }
    static object DraftField(string name) { object draft = Field("inline"); return draft.GetType().GetField(name).GetValue(draft); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject element) {
        yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
    static Button Button(string name) { return Descendants(root).OfType<Button>().First(x => AutomationProperties.GetName(x) == name); }
    static string Text(FrameworkElement element) { var box = element as TextBox; return box != null ? box.Text : ((TextBlock)element).Text; }
    static FrameworkElement Display(string name, string text) {
        return Descendants(Button(name)).OfType<FrameworkElement>().First(x => (x is TextBox || x is TextBlock) && Text(x) == text);
    }
    static void Layout() { root.Measure(new Size(layoutWidth, layoutHeight)); root.Arrange(new Rect(0, 0, layoutWidth, layoutHeight)); root.UpdateLayout(); }
    static Rect Bounds(FrameworkElement element) { return element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
    static FrameworkElement Host(FrameworkElement element, bool topic) {
        for (DependencyObject item = element; item != null; item = VisualTreeHelper.GetParent(item)) {
            var border = item as Border;
            if (!topic && border != null && border.Background is LinearGradientBrush) return border;
            var grid = item as Grid;
            if (topic && grid != null && grid.Children.OfType<Button>().Any(x => AutomationProperties.GetName(x).StartsWith("Add to "))) return grid;
        }
        throw new Exception("Cannot locate text host.");
    }
    static BitmapSource RenderRoot() {
        double width = Math.Max(1, root.ActualWidth), height = Math.Max(1, root.ActualHeight);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi), (int)Math.Ceiling(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(root);
        // Detach an immutable pixel snapshot before another render or a change
        // to the visual tree can update any retained WPF composition resource.
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var snapshot = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96 * dpi, 96 * dpi, PixelFormats.Pbgra32, null, pixels, bitmap.PixelWidth * 4);
        snapshot.Freeze(); return snapshot;
    }
    static void Save(BitmapSource image, string path) {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(path)) encoder.Save(file);
    }
    static Metrics Capture(FrameworkElement element, bool topic, string name) {
        Layout(); var host = Host(element, topic); var result = new Metrics { Control = Bounds(element), Host = Bounds(host) };
        var transform = element.TransformToAncestor(root); string text = Text(element);
        var box = element as TextBox; var block = element as TextBlock;
        TextPointer start = block == null ? null : block.ContentStart;
        if (start != null) for (int i = 0; i < 8 && start.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text; i++) {
            start = start.GetNextContextPosition(LogicalDirection.Forward); if (start == null) break;
        }
        int previousLine = -1;
        for (int index = 0; index < text.Length; index++) {
            Rect rect = Rect.Empty;
            if (box != null) rect = box.GetRectFromCharacterIndex(index, false);
            else if (start != null) { var pointer = start.GetPositionAtOffset(index, LogicalDirection.Forward); if (pointer != null) rect = pointer.GetCharacterRect(LogicalDirection.Forward); }
            if (rect.IsEmpty) continue;
            rect = transform.TransformBounds(rect);
            // Font fallback can give Latin and CJK different caret heights on
            // the same visual line; use TextBox's actual line index instead.
            int line = box == null ? -1 : box.GetLineIndexFromCharacterIndex(index);
            if (box != null ? line != previousLine : result.Characters.Count == 0 || Math.Abs(result.Characters.Last().Top - rect.Top) > .25) result.Characters.Add(rect);
            previousLine = line;
        }
        // Render the whole root directly; a detached host VisualBrush can
        // inherit a descendant text view clip in the wrong coordinate space.
        var bitmap = RenderRoot(); double opacity = element.Opacity;
        int cropLeft = Math.Max(0, (int)Math.Floor(result.Host.Left * dpi)), cropTop = Math.Max(0, (int)Math.Floor(result.Host.Top * dpi));
        int cropRight = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(result.Host.Right * dpi)), cropBottom = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(result.Host.Bottom * dpi));
        Save(new CroppedBitmap(bitmap, new Int32Rect(cropLeft, cropTop, cropRight - cropLeft, cropBottom - cropTop)), System.IO.Path.Combine(output, name + ".png"));
        if (name.Contains("dpi200") && name.Contains("wrapped-latin")) Save(bitmap, System.IO.Path.Combine(output, name + "-root.png"));
        element.Opacity = 0; var background = RenderRoot(); element.Opacity = opacity;
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var backdrop = new byte[pixels.Length]; background.CopyPixels(backdrop, background.PixelWidth * 4, 0);
        double font = box != null ? box.FontSize : block.FontSize;
        int bandTop = -1, bandBottom = -1, minX = int.MaxValue, maxX = -1, bandLine = -1;
        Action finishBand = delegate {
            if (bandTop < 0) return;
            result.GlyphLines.Add(new Rect(minX / dpi, bandTop / dpi, (maxX - minX + 1) / dpi, (bandBottom - bandTop + 1) / dpi));
            bandTop = bandBottom = -1; minX = int.MaxValue; maxX = -1;
        };
        for (int y = cropTop; y < cropBottom; y++) {
            int rowMin = int.MaxValue, rowMax = -1;
            for (int x = cropLeft; x < cropRight; x++) {
                int offset = (y * bitmap.PixelWidth + x) * 4;
                if (Math.Abs(pixels[offset] - backdrop[offset]) > 40 || Math.Abs(pixels[offset + 1] - backdrop[offset + 1]) > 40 || Math.Abs(pixels[offset + 2] - backdrop[offset + 2]) > 40 || Math.Abs(pixels[offset + 3] - backdrop[offset + 3]) > 40) { rowMin = Math.Min(rowMin, x); rowMax = x; }
            }
            if (rowMax < 0) continue;
            int observedLine = 0;
            if (box != null && result.Characters.Count > 0) {
                // Closely spaced CJK lines can have only 1–2 empty raster rows.
                // Assign ink by WPF line centers rather than a gap threshold.
                double rowY = (y + .5) / dpi;
                while (observedLine + 1 < result.Characters.Count && rowY > (result.Characters[observedLine].Top + result.Characters[observedLine].Height / 2 + result.Characters[observedLine + 1].Top + result.Characters[observedLine + 1].Height / 2) / 2) observedLine++;
                if (bandBottom >= 0 && observedLine != bandLine) finishBand();
            } else if (bandBottom >= 0 && y - bandBottom > Math.Max(3, Math.Ceiling(font * dpi * .25))) finishBand();
            bandLine = observedLine;
            if (bandTop < 0) bandTop = y; bandBottom = y; minX = Math.Min(minX, rowMin); maxX = Math.Max(maxX, rowMax);
        }
        finishBand();
        if (box != null && text.Length > 0 && result.GlyphLines.Count != result.Characters.Count) throw new Exception(name + ": raster has " + result.GlyphLines.Count + " lines but character layout has " + result.Characters.Count + ". Check full-root render for clipping.");
        return result;
    }
    static string N(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    static void Compare(Metrics before, Metrics after, string name, string phase, bool empty) {
        double gx = 0, gy = 0, cx = 0, cy = 0;
        bool glyphs = before.GlyphLines.Count == after.GlyphLines.Count;
        if (!empty && before.GlyphLines.Count > 0 && after.GlyphLines.Count > 0) {
            gx = after.GlyphLines[0].Left - before.GlyphLines[0].Left; gy = after.GlyphLines[0].Top - before.GlyphLines[0].Top;
            for (int i = 0; i < Math.Min(before.GlyphLines.Count, after.GlyphLines.Count); i++) glyphs &= Math.Abs(after.GlyphLines[i].Left - before.GlyphLines[i].Left) * dpi <= .8 && Math.Abs(after.GlyphLines[i].Top - before.GlyphLines[i].Top) * dpi <= .8;
        } else if (!empty) glyphs = false;
        if (before.Characters.Count > 0 && after.Characters.Count > 0) { cx = after.Characters[0].Left - before.Characters[0].Left; cy = after.Characters[0].Top - before.Characters[0].Top; }
        double[] differences = { after.Control.Left - before.Control.Left, after.Control.Top - before.Control.Top, after.Control.Width - before.Control.Width, after.Control.Height - before.Control.Height, after.Host.Left - before.Host.Left, after.Host.Top - before.Host.Top, after.Host.Width - before.Host.Width, after.Host.Height - before.Host.Height };
        report.Add(name + "," + phase + "," + string.Join(",", differences.Select(N)) + "," + N(gx) + "," + N(gy) + "," + before.GlyphLines.Count + "," + after.GlyphLines.Count + "," + N(cx) + "," + N(cy));
        bool stableHost = differences.Skip(4).All(x => Math.Abs(x) * dpi <= .8);
        bool stableField = differences.Take(4).All(x => Math.Abs(x) * dpi <= .8);
        bool passed = glyphs && stableHost && stableField;
        if (!passed) failures++;
        Console.WriteLine((passed ? "PASS " : "FAIL ") + name + " " + phase + ": glyph(" + N(gx) + "," + N(gy) + "), field(" + string.Join(",", differences.Take(4).Select(N)) + "), card dh=" + N(differences[7]) + ", lines " + before.GlyphLines.Count + "/" + after.GlyphLines.Count);
    }
    static void Run(string kind, Sample sample, double scale, double zoom) {
        cases++; dpi = scale;
        typeof(MainWindow).GetField("inline", Private).SetValue(window, null);
        var config = (Settings)Field("config"); config.Zoom = zoom; Call("ApplyZoom");
        var watchingTitle = typeof(Settings).GetField("WatchingTitle");
        if (watchingTitle != null) watchingTitle.SetValue(config, kind == "watch-title" ? sample.Value : NoteStore.ReadingGroup);
        VisualTreeHelper.SetRootDpi(root, new DpiScale(scale, scale));
        string group = kind == "topic" ? sample.Value.Length == 0 ? NoteDocument.NewEmptyGroup() : sample.Value : "Plans";
        var watching = new Entry { Group = NoteStore.ReadingGroup, Title = kind == "watch-name" ? sample.Value : "Reference", Link = kind == "watch-link" ? sample.Value : "https://example.test/read" };
        var task = new Entry { Group = group, Title = kind == "task" ? sample.Value : "Task" };
        string text = "# Test\n\n## " + NoteStore.ReadingGroup + "\n" + NoteDocument.Serialize(watching, "\n") + "\n\n## " + group + "\n" + NoteDocument.Serialize(task, "\n") + "\n";
        File.WriteAllText(store.CurrentPath, text, new System.Text.UTF8Encoding(false)); window.Refresh(); Layout();
        bool topic = kind == "topic" || kind == "watch-title", link = kind == "watch-link";
        string editedGroup = kind == "watch-title" ? NoteStore.ReadingGroup : group;
        var document = (NoteDocument)Field("document");
        Entry entry = topic ? null : document.Entries.First(x => x.Group == (kind == "task" ? group : NoteStore.ReadingGroup));
        string label = topic ? "Rename " + (string)Call("GroupLabel", editedGroup) : "Edit " + (link ? "link " : "") + (string.IsNullOrWhiteSpace(entry.Title) && string.IsNullOrWhiteSpace(entry.Link) ? "empty item 1 in " + (string)Call("GroupLabel", entry.Group) : !string.IsNullOrWhiteSpace(entry.Title) ? entry.Title : entry.Link);
        string name = kind + "-" + sample.Name + "-dpi" + N(scale * 100) + "-zoom" + N(zoom) + scenarioSuffix;
        var before = Capture(Display(label, sample.Value), topic, name + "-display");
        if (topic) Call("BeginTopic", editedGroup); else if (link) Call("EditLink", entry); else window.Edit(entry, entry.Group);
        Layout(); var editing = Capture((FrameworkElement)DraftField(link ? "LinkInput" : "Input"), topic, name + "-edit");
        Compare(before, editing, name, "enter", sample.Value.Length == 0);
        if (!(bool)Call("FinishInline", false)) throw new Exception("The isolated unchanged edit did not finish.");
        Layout(); var after = Capture(Display(label, sample.Value), topic, name + "-exit");
        Compare(before, after, name, "exit", sample.Value.Length == 0);
        if (File.ReadAllText(store.CurrentPath) != text) throw new Exception("An unchanged edit modified fixture content.");
    }
    static Sample[] Samples(string kind) {
        if (kind == "watch-link") return new[] { new Sample("empty", ""), new Sample("latin", "https://example.test/read"), new Sample("cjk", "https://example.test/阅读"), new Sample("wrapped-latin", "https://example.test/reading-notes/long-reference-path/another-topic-to-review/later-in-the-week"), new Sample("wrapped-cjk", "https://example.test/阅读计划与长期任务/资料整理以及论文阅读/本周需要完成的重点项目与链接") };
        string latin = kind == "topic" || kind == "watch-title" ? "Long project title with several meaningful words" : "A detailed project task that wraps across several lines while keeping every word in its original position";
        return new[] { new Sample("empty", ""), new Sample("latin", "Project notes"), new Sample("cjk", "研究计划"), new Sample("wrapped-latin", latin), new Sample("wrapped-cjk", "研究计划与阅读安排：本周需要逐项完成的长期任务以及所有重要参考资料") };
    }
    [STAThread] static int Main(string[] args) {
        output = System.IO.Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            string state = System.IO.Path.Combine(output, "state"); store = new NoteStore(System.IO.Path.Combine(state, "vault"), System.IO.Path.Combine(state, "backups")); store.Initialize();
            window = new MainWindow(store, new Settings { Width = 900, Height = 640, Pinned = false }, state);
            root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
            root.SetValue(TextElement.FontFamilyProperty, window.FontFamily); root.SetValue(TextElement.FontSizeProperty, window.FontSize); root.SetValue(TextElement.ForegroundProperty, window.Foreground);
            root.UseLayoutRounding = window.UseLayoutRounding; root.SnapsToDevicePixels = window.SnapsToDevicePixels; TextOptions.SetTextFormattingMode(root, TextOptions.GetTextFormattingMode(window));
            var kinds = new List<string> { "topic", "task", "watch-name", "watch-link" };
            if (typeof(Settings).GetField("WatchingTitle") != null) kinds.Add("watch-title");
            if (args.Length > 1 && args[1] == "typography-board") TypographyBoard();
            else if (args.Length > 1 && args[1] == "transitions") ExtendedTransitions();
            else if (args.Length > 1 && args[1] == "extended") ExtendedAudit(kinds);
            else foreach (string kind in kinds) {
                foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) foreach (var sample in Samples(kind)) Run(kind, sample, scale, 1);
                Run(kind, Samples(kind).First(x => x.Name == "wrapped-latin"), 2, 1.25);
            }
            Console.WriteLine(cases + " text cases; " + failures + " failed phase comparisons."); return failures == 0 ? 0 : 1;
        } catch (Exception ex) { Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); if (ex.InnerException != null) Console.Error.WriteLine(ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message); return 2; }
        finally { File.WriteAllLines(System.IO.Path.Combine(output, "metrics.csv"), report); if (window != null) { typeof(MainWindow).GetField("inline", Private).SetValue(window, null); window.Exit(); } app.Shutdown(); }
    }
}
