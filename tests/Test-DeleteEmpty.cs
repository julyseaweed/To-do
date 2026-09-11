using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shike;

// Genuine routed key events and rendered WPF cards, with no visible window.
class DeleteEmptyTests {
    sealed class OfflineSource : PresentationSource {
        public override Visual RootVisual { get; set; }
        public override bool IsDisposed { get { return false; } }
        protected override CompositionTarget GetCompositionTargetCore() { return null; }
    }
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Fixture = "# Test\n\n## Watching list\n- [ ] Watch before\n- [ ]\n- [ ] Watch after\n\n## Plans\n- [ ] Alpha\n- [ ]\n- [ ] Omega\n- [ ] Tail\n";
    static MainWindow window;
    static FrameworkElement root;
    static NoteStore store;
    static int checks;
    static object Draft { get { return Field("inline"); } }
    static object Field(string name) { return typeof(MainWindow).GetField(name, Private).GetValue(window); }
    static void SetField(string name, object value) { typeof(MainWindow).GetField(name, Private).SetValue(window, value); }
    static object DraftField(string name) { return Draft.GetType().GetField(name).GetValue(Draft); }
    static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, Private).Invoke(window, args); }
    static TextBox Input(bool link = false) { return (TextBox)DraftField(link ? "LinkInput" : "Input"); }
    static string FileText { get { return File.ReadAllText(store.CurrentPath); } }
    static void Check(bool condition, string label) {
        if (!condition) throw new Exception(label);
        checks++; Console.WriteLine("PASS " + label);
    }
    static void Layout() { root.Measure(new Size(1000, 600)); root.Arrange(new Rect(0, 0, 1000, 600)); root.UpdateLayout(); }
    static void Reset(string text = null) {
        SetField("inline", null); SetField("inlineRemovalUndo", null); SetField("inlineRemovalKey", System.Windows.Input.Key.None);
        SetField("undoBefore", null); SetField("undoAfter", null);
        File.WriteAllText(store.CurrentPath, text ?? Fixture, new System.Text.UTF8Encoding(false)); window.Refresh(); Layout();
    }
    static Entry[] Entries(string group) { return ((NoteDocument)Field("document")).Entries.Where(x => x.Group == group).ToArray(); }
    static void Edit(string group, int index, bool link = false) {
        Entry entry = Entries(group)[index];
        if (link) Call("EditLink", entry); else window.Edit(entry, group);
        Layout();
    }
    static KeyEventArgs Key(TextBox input, Key key, bool repeat = false) {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, new OfflineSource(), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = input };
        if (repeat) typeof(KeyEventArgs).GetField("_isRepeat", Private).SetValue(e, true);
        input.RaiseEvent(e); Layout(); return e;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject element) {
        yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(element, i))) yield return child;
    }
    static Border Card(DependencyObject item) {
        for (var element = item; element != null; element = VisualTreeHelper.GetParent(element)) {
            var border = element as Border;
            if (border != null && border.Background is LinearGradientBrush) return border;
        }
        throw new Exception("Cannot locate the actual card surface.");
    }
    static Border NamedCard(string name) { return Card(Descendants(root).First(x => AutomationProperties.GetName(x) == name)); }
    static int ColoredCards(string group) {
        var list = Descendants(root).OfType<ScrollViewer>().First(x => AutomationProperties.GetName(x) == "Items in " + group);
        return Descendants(list).OfType<Border>().Count(x => x.Background is LinearGradientBrush);
    }
    static double[] Sample(Border card) {
        Layout();
        double width = card.ActualWidth, height = card.ActualHeight;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(card) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual); var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int x = (int)(width * 2 * .7), y = 12, offset = (y * bitmap.PixelWidth + x) * 4;
        double alpha = pixels[offset + 3]; if (alpha == 0) throw new Exception("The card sample is transparent.");
        return new[] { pixels[offset] * 255.0 / alpha, pixels[offset + 1] * 255.0 / alpha, pixels[offset + 2] * 255.0 / alpha };
    }
    static bool SameColor(double[] left, double[] right) { return left.Zip(right, (a, b) => Math.Abs(a - b)).Max() < 3; }
    static void CheckTitle(string title, string label) { Check(Draft != null && ((Entry)DraftField("Original")).Title == title, label); }
    static void ExerciseDeletionAndPaint() {
        Reset();
        var secondColor = Sample(NamedCard("Edit empty item 2 in Plans"));
        var thirdColor = Sample(NamedCard("Edit Omega"));
        Edit("Plans", 1);
        var draft = Draft; var before = FileText;
        Check(!Key(Input(), System.Windows.Input.Key.Back, true).Handled && FileText == before && Draft == draft, "A held key cannot start deleting an empty card");
        Draft.GetType().GetField("Composing").SetValue(Draft, true);
        Check(!Key(Input(), System.Windows.Input.Key.Back).Handled && FileText == before, "IME composition blocks empty-card deletion");
        Draft.GetType().GetField("Composing").SetValue(Draft, false);
        Input().Text = " ";
        Check(!Key(Input(), System.Windows.Input.Key.Delete).Handled && FileText == before, "A literal space is text and does not delete the card");
        Input().Text = "x";
        Check(!Key(Input(), System.Windows.Input.Key.Back).Handled && FileText == before, "The key that removes existing text cannot remove the whole card");
        Input().Text = "";
        Check(Key(Input(), System.Windows.Input.Key.Delete).Handled && Entries("Plans").Select(x => x.Title).SequenceEqual(new[] { "Alpha", "Omega", "Tail" }), "An extra Delete removes exactly the current empty card");
        CheckTitle("Omega", "Delete continues at the following visible item");
        Check(SameColor(secondColor, Sample(Card(Input()))), "The following card takes the deleted middle card's rendered gradient shade");
        Check(SameColor(thirdColor, Sample(NamedCard("Edit Tail"))), "Every subsequent card's rendered shade moves up one row");
        var after = FileText; var neighborText = Input().Text;
        Check(Key(Input(), System.Windows.Input.Key.Delete, true).Handled && FileText == after && Input().Text == neighborText, "Repeated Delete cannot spill into the newly focused nonempty neighbor");
        Call("Undo"); Layout();
        Check(FileText == before, "Undo restores the exact file before empty-card deletion");
        Check(SameColor(secondColor, Sample(NamedCard("Edit empty item 2 in Plans"))) && SameColor(thirdColor, Sample(NamedCard("Edit Omega"))), "Undo restores the original rendered gradient positions");
        Reset(); Edit("Plans", 1); Key(Input(), System.Windows.Input.Key.Back);
        CheckTitle("Alpha", "Backspace continues at the preceding visible item");
        after = FileText; neighborText = Input().Text;
        Check(Key(Input(), System.Windows.Input.Key.Back, true).Handled && FileText == after && Input().Text == neighborText, "Repeated Backspace cannot erase the newly focused predecessor");
        Reset("# Test\n\n## Watching list\n\n## Plans\n- [ ]\n- [x] Hidden done\n- [ ] Visible next\n");
        Edit("Plans", 0); Key(Input(), System.Windows.Input.Key.Back);
        CheckTitle("Visible next", "Deleting the first row falls forward and skips completed hidden items");
        Reset("# Test\n\n## Watching list\n\n## Plans\n- [ ]\n"); Edit("Plans", 0); Key(Input(), System.Windows.Input.Key.Delete);
        Check(Draft == null && Entries("Plans").Length == 0 && ((NoteDocument)Field("document")).Groups.Contains("Plans"), "Deleting the final empty row retains the topic without creating another blank");
        Check(ColoredCards("Plans") == 0 && Descendants(root).OfType<Button>().Any(x => AutomationProperties.GetName(x) == "Add to Plans"), "Deleting the final card visibly empties its column while keeping the heading add button");
        Reset("# Test\n\n## Watching list\n\n## Plans\n- [ ] Previous item\n- [ ]\n"); Edit("Plans", 1); Key(Input(), System.Windows.Input.Key.Delete);
        CheckTitle("Previous item", "Delete on the final row falls back to the preceding visible item");
    }
    static void ExerciseSafety() {
        Reset(); Edit("Plans", 0); var before = FileText; Input().Text = "";
        Key(Input(), System.Windows.Input.Key.Back); Call("Undo"); Layout();
        Check(FileText == before, "Clear-then-delete Undo restores the original title, not an intermediate blank");
        Reset("# Test\n\n## Watching list\n- [ ] [](<https://example.test/watch>)\n\n## Plans\n");
        Edit("Watching list", 0); before = FileText;
        Check(!Key(Input(), System.Windows.Input.Key.Back).Handled && FileText == before, "An empty title cannot delete a card that still contains a URL");
        Reset("# Test\n\n## Watching list\n- [ ]\n  > Keep this note.\n\n## Plans\n");
        Edit("Watching list", 0); before = FileText;
        Check(!Key(Input(), System.Windows.Input.Key.Delete).Handled && FileText == before, "A note-only card is protected from empty-card deletion");
        Reset(); Edit("Watching list", 1, true); Key(Input(true), System.Windows.Input.Key.Back);
        Check(Entries("Watching list").Length == 2 && DraftField("LinkInput") != null, "An entirely empty watching card deletes from its link field and preserves link editing");
        Reset(); Edit("Plans", 1); before = FileText;
        var external = before.Replace("- [ ] Alpha", "- [ ] Alpha changed externally"); File.WriteAllText(store.CurrentPath, external);
        Key(Input(), System.Windows.Input.Key.Delete);
        Check(FileText.Contains("Alpha changed externally") && Entries("Plans").Length == 3, "Deleting a matching row preserves unrelated external edits");
        Call("Undo"); Layout(); Check(FileText == external, "Deletion Undo also preserves the external changes that preceded it");
        Reset(); Edit("Plans", 1); var draft = Draft;
        external = FileText.Replace("- [ ] Alpha\n- [ ]\n", "- [ ] Alpha\n- [ ] Externally filled\n"); File.WriteAllText(store.CurrentPath, external);
        Check(Key(Input(), System.Windows.Input.Key.Delete).Handled && FileText == external && Draft == draft && !string.IsNullOrEmpty((string)DraftField("Error")), "External modification blocks deletion, retaining the editor and the external file");
        Reset("# Test\n\n## Watching list\n\n## Plans\n- [ ]\n- [ ]\n- [ ] Tail\n"); Edit("Plans", 1); draft = Draft;
        external = FileText.Replace("- [ ]\n- [ ]\n", "- [ ]\n"); File.WriteAllText(store.CurrentPath, external);
        Check(Key(Input(), System.Windows.Input.Key.Back).Handled && FileText == external && Draft == draft, "An external duplicate-count change cannot delete a different blank row");
    }
    static void ExerciseTopicDeletion() {
        const string topics = "# Test\n\n## Watching list\n- [ ] Watch kept\n\n## Plans\n- [ ] [Plan first](<https://example.test/plan>)\n  > Plan note\n- [x] Completed plan\n\n## Other\n- [ ] Other first\n- [ ] Other second\n\n## Last\n- [ ] Last first\n";
        foreach (var key in new[] { System.Windows.Input.Key.Back, System.Windows.Input.Key.Delete }) {
            Reset(topics);
            var before = FileText;
            var firstColor = Sample(NamedCard("Edit Plan first"));
            var secondColor = Sample(NamedCard("Edit Other first"));
            var watchingColor = Sample(NamedCard("Edit Watch kept"));
            Call("BeginTopic", "Plans"); Layout(); var draft = Draft; var input = Input();
            Check(!Key(input, key).Handled && FileText == before && Draft == draft, key + " on a named topic only edits its text");
            input.Text = "";
            Check(FileText == before && ((NoteDocument)Field("document")).Groups.Contains("Plans"), "Clearing a topic name alone retains its column and contents");
            Check(!Key(input, key, true).Handled && Draft == draft && FileText == before, "Holding " + key + " after clearing a name cannot delete its topic");
            Draft.GetType().GetField("Composing").SetValue(Draft, true);
            Check(!Key(input, key).Handled && FileText == before, "IME composition protects an empty topic from " + key);
            Draft.GetType().GetField("Composing").SetValue(Draft, false);
            Check(!Key(input, System.Windows.Input.Key.ImeProcessed).Handled && FileText == before, "An IME-processed key cannot remove a topic");
            input.Text = " ";
            Check(!Key(input, key).Handled && FileText == before, "A topic containing a literal space is not empty for deletion");
            input.Text = "";
            Check(Key(input, key).Handled && Draft == null && !((NoteDocument)Field("document")).Groups.Contains("Plans") && !FileText.Contains("Plan note") && !FileText.Contains("Completed plan"), "A fresh " + key + " removes the complete topic including its link, note and completed items");
            Check(SameColor(firstColor, Sample(NamedCard("Edit Other first"))) && SameColor(secondColor, Sample(NamedCard("Edit Last first"))), "Following topics take the preceding columns' rendered colors after deletion");
            Check(SameColor(watchingColor, Sample(NamedCard("Edit Watch kept"))), "Watching retains its rendered burgundy color after topic deletion");
            var after = FileText; Key(input, key, true);
            Check(Draft == null && FileText == after, "Repeated " + key + " cannot start editing or remove another topic");
            Call("Undo"); Layout();
            Check(FileText == before && SameColor(firstColor, Sample(NamedCard("Edit Plan first"))) && SameColor(secondColor, Sample(NamedCard("Edit Other first"))), "Topic Undo restores the original name, full contents, order and colors");
        }
        Reset(topics); Call("BeginTopic", new object[] { null }); Layout();
        string emptyGroup = (string)DraftField("Group"), emptyBefore = FileText;
        Key(Input(), System.Windows.Input.Key.Delete);
        Check(Draft == null && !((NoteDocument)Field("document")).Groups.Contains(emptyGroup), "A newly added unnamed topic can be deleted immediately");
        Call("Undo"); Layout(); Check(FileText == emptyBefore, "Undo restores the same unnamed topic identity and position");

        Reset(topics); Call("BeginTopic", NoteStore.ReadingGroup); Layout(); var watchingBefore = FileText; Input().Text = "";
        Check(!Key(Input(), System.Windows.Input.Key.Back).Handled && !Key(Input(), System.Windows.Input.Key.Delete).Handled && FileText == watchingBefore && Draft != null, "The fixed Watching column can be renamed but cannot be removed by either deletion key");

        Reset(topics); Call("BeginTopic", "Plans"); Layout(); Input().Text = "";
        string external = FileText.Replace("Other first", "Other changed externally"); File.WriteAllText(store.CurrentPath, external);
        Key(Input(), System.Windows.Input.Key.Delete);
        Check(Draft == null && FileText.Contains("Other changed externally") && !((NoteDocument)Field("document")).Groups.Contains("Plans"), "Deleting a topic preserves unrelated external changes in another column");
        Call("Undo"); Layout(); Check(FileText == external, "Topic Undo retains external changes that preceded deletion");
        Reset(topics); Call("BeginTopic", "Plans"); Layout(); Input().Text = ""; var conflictedDraft = Draft;
        external = FileText.Replace("Plan note", "Plan note changed externally"); File.WriteAllText(store.CurrentPath, external);
        Check(Key(Input(), System.Windows.Input.Key.Delete).Handled && FileText == external && Draft == conflictedDraft && !string.IsNullOrEmpty((string)DraftField("Error")), "External edits inside the target topic block deletion and retain the editor");

        const string sections = "# Test\n\n## Watching list\n\n## Plans   \n- [ ] First section\n\n## Other\n- [ ] Other kept\n```markdown\n## Plans\n- [ ] Literal example\n```\n\n## Plans\n- [ ] Second section\n\n## Last\n- [ ] Last kept\n";
        Reset(sections); Call("BeginTopic", "Plans"); Layout(); Input().Text = ""; Key(Input(), System.Windows.Input.Key.Delete);
        Check(Draft == null && !((NoteDocument)Field("document")).Groups.Contains("Plans") && !FileText.Contains("First section") && !FileText.Contains("Second section"), "Deletion removes all real sections of a topic, including headings with trailing spaces");
        Check(FileText.Contains("```markdown\n## Plans\n- [ ] Literal example\n```") && FileText.Contains("Other kept") && FileText.Contains("Last kept"), "A matching heading inside another topic's fenced code is preserved verbatim");
        Call("Undo"); Layout(); Check(FileText == sections, "Undo restores duplicate topic sections and exact original Markdown");
    }
    static void RecordFailure(string log, string stage, Exception exception) {
        // WPF teardown can make Exception.ToString() throw while formatting a
        // stack trace. Log raw properties before shutdown instead.
        var lines = new List<string> { "FAIL stage=" + stage };
        for (int depth = 0; exception != null && depth < 8; depth++, exception = exception.InnerException) {
            lines.Add("Type=" + exception.GetType().FullName);
            try { lines.Add("Message=" + exception.Message); } catch { lines.Add("Message=<unavailable>"); }
            try { lines.Add("HResult=" + exception.HResult.ToString("X8")); } catch { }
        }
        File.AppendAllLines(log, lines); foreach (string line in lines) Console.Error.WriteLine(line);
    }
    [STAThread] static int Main(string[] args) {
        string state = Path.GetFullPath(args[0]); Directory.CreateDirectory(state);
        string log = Path.Combine(state, "test-diagnostic.txt"), stage = "application";
        Application app = null; int result = 0;
        try {
            File.WriteAllText(log, "Starting offline deletion regression\n");
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            stage = "note store"; File.AppendAllText(log, stage + "\n");
            store = new NoteStore(Path.Combine(state, "vault"), Path.Combine(state, "backups")); store.Initialize();
            stage = "MainWindow construction"; File.AppendAllText(log, stage + "\n");
            window = new MainWindow(store, new Settings { Width = 1000, Height = 600, Pinned = false }, state);
            stage = "detached resources"; File.AppendAllText(log, stage + "\n");
            root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
            stage = "modifier guard"; var modifiers = Keyboard.Modifiers; File.AppendAllText(log, stage + ": " + modifiers + "\n");
            Check(modifiers == ModifierKeys.None, "Offline fixture starts without pressed modifier keys (actual " + modifiers + ")");
            stage = "deletion scenarios";
            ExerciseDeletionAndPaint(); ExerciseSafety(); ExerciseTopicDeletion();
            Console.WriteLine(checks + " empty-card and topic deletion checks passed without showing a window.");
            File.AppendAllText(log, "PASS " + checks + " checks\n");
        } catch (Exception ex) { result = 1; RecordFailure(log, stage, ex); }
        finally {
            try { if (window != null) { SetField("inline", null); window.Exit(); } if (app != null) app.Shutdown(); }
            catch (Exception ex) { result = 1; RecordFailure(log, "cleanup", ex); }
        }
        return result;
    }
}
