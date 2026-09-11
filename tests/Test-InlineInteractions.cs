using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shike;

// Rendered glyph hit tests and routed input events; never shows a window.
static class InlineInteractionTests {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Fixture = "# Test\n\n## Watching list\n- [ ] [Watch](<https://example.test/watch>)\n\n## Plans\n- [ ] First\n- [ ] Duplicate\n- [ ] Duplicate\n- [ ]\n";
    sealed class OfflineSource : PresentationSource {
        public override Visual RootVisual { get; set; }
        public override bool IsDisposed { get { return false; } }
        protected override CompositionTarget GetCompositionTargetCore() { return null; }
    }
    static MainWindow window;
    static FrameworkElement root;
    static NoteStore store;
    static int checks, failures;
    [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] static extern bool SetKeyboardState(byte[] state);
    static object Field(string name) { return typeof(MainWindow).GetField(name, Hidden).GetValue(window); }
    static void SetField(string name, object value) { typeof(MainWindow).GetField(name, Hidden).SetValue(window, value); }
    static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, Hidden).Invoke(window, args); }
    static object Draft { get { return Field("inline"); } }
    static object DraftField(string name) { return Draft.GetType().GetField(name).GetValue(Draft); }
    static void DraftSet(string name, object value) { Draft.GetType().GetField(name).SetValue(Draft, value); }
    static TextBox Input { get { return (TextBox)DraftField("Input"); } }
    static NoteDocument Document { get { return (NoteDocument)Field("document"); } }
    static string FileText { get { return File.ReadAllText(store.CurrentPath); } }
    static void Check(bool condition, string label) { checks++; if (!condition) failures++; Console.WriteLine((condition ? "PASS " : "FAIL ") + label); }
    static void Layout() { root.Measure(new Size(1000, 620)); root.Arrange(new Rect(0, 0, 1000, 620)); root.UpdateLayout(); }
    static void Pump(int milliseconds) {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); Layout();
    }
    static void Reset(string text = Fixture) {
        SetField("inline", null); SetField("inlineRemovalUndo", null); SetField("undoBefore", null); SetField("undoAfter", null);
        File.WriteAllText(store.CurrentPath, text); window.Refresh(); Layout();
    }
    static KeyEventArgs Key(TextBox input, Key key) {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, new OfflineSource(), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = input };
        input.RaiseEvent(e); Layout(); return e;
    }
    static void Composition(TextBox input, bool start) {
        var composition = new TextComposition(InputManager.Current, input, start ? "" : "finished");
        var e = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = start ? TextCompositionManager.PreviewTextInputStartEvent : TextCompositionManager.PreviewTextInputEvent, Source = input };
        input.RaiseEvent(e);
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject node) {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    static Button Button(string name) { return Descendants(root).OfType<Button>().First(x => AutomationProperties.GetName(x) == name); }
    static void CompositionCases() {
        Reset(); window.Edit(Document.Entries.First(x => x.Title == "First"), "Plans"); Layout();
        Input.Text = "Uncommitted text"; var draft = Draft; var input = Input; var before = FileText;
        Composition(input, true);
        Check((bool)DraftField("Composing"), "Composition start marks the active draft as composing");
        Check(!Key(input, System.Windows.Input.Key.Enter).Handled && Draft == draft && FileText == before, "IME candidate Enter cannot insert or save a checklist row");
        Check(!Key(input, System.Windows.Input.Key.Escape).Handled && Draft == draft && input.Text == "Uncommitted text" && FileText == before, "IME candidate Escape leaves the editor and typed draft intact");
        // Restart to isolate the blur case even when the old Escape bug fails.
        Reset(); window.Edit(Document.Entries.First(x => x.Title == "First"), "Plans"); Layout();
        Input.Text = "Composed text"; draft = Draft; input = Input; before = FileText;
        Composition(input, true); Call("ScheduleInlineBlur", draft); Pump(90);
        Check(Draft == draft && FileText == before, "Losing focus during composition never saves incomplete candidate text");
        DraftSet("Composing", false); Pump(100);
        Check(Draft == null && store.Load().Entries.Any(x => x.Title == "Composed text"), "Deferred blur resumes when composition finishes after focus has moved");
        Reset(); window.Edit(Document.Entries.First(x => x.Title == "First"), "Plans"); Layout();
        Input.Text = "Canceled draft"; before = FileText;
        Check(Key(Input, System.Windows.Input.Key.Escape).Handled && Draft == null && FileText == before, "Escape outside composition still cancels only unsaved text");
    }
    static void CompositionShortcutCases() {
        Reset(); window.Edit(Document.Entries.First(x => x.Title == "First"), "Plans"); Layout();
        Input.Text = "Candidate text"; Composition(Input, true);
        var draft = Draft; var before = FileText; double zoom = ((Settings)Field("config")).Zoom;
        var originalKeys = new byte[256];
        if (!GetKeyboardState(originalKeys)) throw new Exception("Cannot snapshot the isolated thread's keyboard state.");
        try {
            // This changes this thread's GetKeyState table only. It does not
            // send keyboard input or affect other applications' input state.
            var keys = new byte[256]; keys[0x11] = 0x80; keys[0xA2] = 0x80;
            if (!SetKeyboardState(keys) || Keyboard.Modifiers != ModifierKeys.Control) throw new Exception("Cannot establish a thread-local Ctrl fixture.");
            foreach (Key key in new[] { System.Windows.Input.Key.N, System.Windows.Input.Key.Z, System.Windows.Input.Key.OemPlus, System.Windows.Input.Key.OemMinus, System.Windows.Input.Key.D0 }) {
                var e = new KeyEventArgs(Keyboard.PrimaryDevice, new OfflineSource(), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = Input };
                Call("OnKey", window, e);
                Check(!e.Handled && Draft == draft && Input.Text == "Candidate text" && FileText == before && ((Settings)Field("config")).Zoom == zoom, "Window preview leaves Ctrl+" + key + " to an active IME composition");
            }
        } finally { if (!SetKeyboardState(originalKeys)) throw new Exception("Cannot restore the isolated thread's keyboard table."); }
        DraftSet("Composing", false); Call("CancelInline"); Layout();
    }
    static void SwitchingCases() {
        Reset(); var duplicates = Document.Entries.Where(x => x.Title == "Duplicate").ToArray();
        window.Edit(duplicates[0], "Plans"); Layout(); Input.Text = "Renamed first duplicate";
        window.Edit(duplicates[1], "Plans"); Layout();
        Check(((Entry)DraftField("Original")).Title == "Duplicate" && ((Entry)DraftField("Original")).Occurrence == 0 && store.Load().Entries.Count(x => x.Title == "Renamed first duplicate") == 1, "Switching between duplicate rows saves the first and opens the intended second row");
        Input.Text = "Renamed second duplicate"; Call("FinishInline", false); Layout();
        Check(store.Load().Entries.Select(x => x.Title).Contains("Renamed first duplicate") && store.Load().Entries.Any(x => x.Title == "Renamed second duplicate"), "Both switched duplicate edits persist independently");
        Reset(); Call("BeginTopic", "Plans"); Layout(); Input.Text = "Renamed plans";
        Button("Add to Plans").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Layout();
        Check((string)DraftField("Group") == "Renamed plans" && store.Load().Groups.Contains("Renamed plans"), "Adding while renaming uses the new topic identity");
        var count = store.Load().Entries.Count; Key(Input, System.Windows.Input.Key.Enter); Key(Input, System.Windows.Input.Key.Enter); Layout();
        Check(store.Load().Entries.Count == count + 2 && ((Entry)DraftField("Original")).Title == "", "Repeated Enter persists exactly one additional empty row each time");
        var before = FileText; Key(Input, System.Windows.Input.Key.Escape);
        Check(FileText == before && Draft == null, "Escape retains all already persisted empty rows");
        Reset(); window.Edit(Document.Entries.First(x => x.Title == "First"), "Plans"); Layout(); Input.Text = "My draft";
        var current = Draft; var target = Document.Entries.First(x => x.Title == "Duplicate");
        var external = FileText.Replace("- [ ] First", "- [ ] Changed elsewhere"); File.WriteAllText(store.CurrentPath, external);
        window.Edit(target, "Plans"); Layout();
        Check(Draft == current && Input.Text == "My draft" && FileText == external && !string.IsNullOrEmpty((string)DraftField("Error")), "A conflict while switching keeps the draft and preserves the external edit");
    }
    static int NativeInsertion(TextBox box, Point point) {
        var method = typeof(TextBox).GetMethod("GetTextPositionFromPointInternal", Hidden);
        object pointer = method.Invoke(box, new object[] { point, true });
        if (pointer == null) return 0;
        return (int)pointer.GetType().GetProperty("Offset", Hidden).GetValue(pointer, null);
    }
    static int AppInsertion(TextBox box, Point point) {
        var method = typeof(MainWindow).GetMethod("CaretFromPoint", BindingFlags.Static | BindingFlags.NonPublic);
        // The baseline callback assigned GetCharacterIndexFromPoint directly.
        return method == null ? Math.Max(0, box.GetCharacterIndexFromPoint(point, true)) : (int)method.Invoke(null, new object[] { box, point });
    }
    static void CaretCases() {
        string[] samples = { "Alpha middle end", "Wide WWW and thin iii", "Long wrapped text across several visible rows so clicking later lines remains precise", "A\U0001F642B cafe\u0301 \u4e2d\u6587 end", "abc \u05e9\u05dc\u05d5\u05dd xyz", "https://example.test/a-long-address/with-several-parts?q=value#fragment" };
        foreach (string text in samples) {
            Reset("# Test\n\n## Watching list\n\n## Plans\n- [ ] " + text + "\n");
            var button = Button("Edit " + text); var box = Descendants(button).OfType<TextBox>().Single();
            int errors = 0, count = 0; string first = "";
            for (int index = 0; index < text.Length; index++) {
                Rect leading = box.GetRectFromCharacterIndex(index, false), trailing = box.GetRectFromCharacterIndex(index, true);
                if (leading.IsEmpty || trailing.IsEmpty || Math.Abs(leading.Top - trailing.Top) > 1 || Math.Abs(leading.X - trailing.X) < .1) continue;
                foreach (double fraction in new[] { .2, .8 }) {
                    var point = new Point(leading.X + (trailing.X - leading.X) * fraction, leading.Top + leading.Height / 2);
                    int native = NativeInsertion(box, point), actual = AppInsertion(box, point); count++;
                    if (actual != native) { errors++; if (first == "") first = " index=" + index + " expected=" + native + " actual=" + actual; }
                }
            }
            Check(count > 8 && errors == 0, "Click insertion matches WPF glyph hit-testing for " + text + " (" + count + " points, " + errors + " mismatches" + first + ")");
            errors = 0; first = "";
            for (double y = 1; y < box.ActualHeight; y += 7) for (double x = 0; x <= box.ActualWidth; x += 13) {
                var point = new Point(x, y); int native = NativeInsertion(box, point), actual = AppInsertion(box, point);
                if (native != actual) { errors++; if (first == "") first = " x=" + x + " y=" + y + " expected=" + native + " actual=" + actual; }
            }
            Check(errors == 0, "Clicks throughout the text field match native insertion edges (" + errors + " mismatches" + first + ")");
            Rect last = box.GetRectFromCharacterIndex(text.Length - 1, true);
            var after = new Point(last.X + 20, last.Top + last.Height / 2);
            Check(AppInsertion(box, after) == NativeInsertion(box, after), "Clicking after the final glyph places the caret at the native insertion point");
        }
    }
    [STAThread] static int Main(string[] args) {
        Application app = null;
        try {
            string state = Path.GetFullPath(args[0]); Directory.CreateDirectory(state);
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            store = new NoteStore(Path.Combine(state, "vault"), Path.Combine(state, "backups")); store.Initialize();
            window = new MainWindow(store, new Settings { Width = 1000, Height = 620, Pinned = false }, state);
            root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
            CompositionCases(); CompositionShortcutCases(); SwitchingCases(); CaretCases();
            Console.WriteLine(checks + " interaction checks, " + failures + " failures; no window shown.");
        } catch (Exception ex) {
            failures++; for (int i = 0; ex != null && i < 6; i++, ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
        } finally {
            if (window != null) { SetField("inline", null); window.Exit(); } if (app != null) app.Shutdown();
        }
        return failures == 0 ? 0 : 1;
    }
}
