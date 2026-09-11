using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shike;

// Real WPF templates and dispatcher timers, without creating a visible HWND.
static class CompletionFeedbackTests {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Fixture = "# Test\n\n## Watching list\n- [ ] [Watch](<https://example.test/watch>)\n\n## Plans\n- [ ] First\n- [ ] Second\n- [ ]\n";
    static MainWindow window;
    static FrameworkElement root;
    static NoteStore store;
    static int checks;
    static object Field(string name) { return typeof(MainWindow).GetField(name, Hidden).GetValue(window); }
    static void SetField(string name, object value) { typeof(MainWindow).GetField(name, Hidden).SetValue(window, value); }
    static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, Hidden).Invoke(window, args); }
    static NoteDocument Document { get { return (NoteDocument)Field("document"); } }
    static IEnumerable<DependencyObject> Descendants(DependencyObject value) {
        yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(value, i))) yield return child;
    }
    static T Named<T>(string name) where T : DependencyObject { return Descendants(root).OfType<T>().FirstOrDefault(x => AutomationProperties.GetName(x) == name); }
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Layout() { root.Measure(new Size(1000, 600)); root.Arrange(new Rect(0, 0, 1000, 600)); root.UpdateLayout(); }
    static void Pump(int milliseconds) {
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        stop.Tick += delegate { stop.Stop(); frame.Continue = false; };
        stop.Start(); Dispatcher.PushFrame(frame); Layout();
    }
    static void Reset(string text = Fixture) {
        SetField("inline", null); File.WriteAllText(store.CurrentPath, text); window.Refresh(); Layout();
    }
    static Entry Task(string title, int index = 0) { return Document.Entries.Where(x => x.Title == title && !x.Done).ElementAt(index); }
    static void Complete(Entry item) {
        var next = new Entry { Group = item.Group, Title = item.Title, Link = item.Link, Note = item.Note, Done = true };
        Check((bool)Call("Change", item, next, item.Group, "Completed"), "Completion transaction succeeds for " + item.Title); Layout();
    }
    static Border Card(DependencyObject item) {
        for (var node = item; node != null; node = VisualTreeHelper.GetParent(node)) { var border = node as Border; if (border != null && border.Background is LinearGradientBrush) return border; }
        throw new Exception("Card missing");
    }
    static string Tint(Border card) { return string.Join("|", ((LinearGradientBrush)card.Background).GradientStops.Select(x => x.Color.ToString())); }
    static void CheckVisibleDot(string title, string label) {
        var check = Named<CheckBox>("Restore " + title);
        var dot = check == null ? null : check.Template.FindName("CompletionDot", check) as Ellipse;
        Check(check != null && check.IsChecked == true && dot != null && dot.Visibility == Visibility.Visible, label);
    }
    static void RunCheckboxPaths() {
        Reset();
        // IsChecked raises the real stored row's Checked event subscription.
        Named<CheckBox>("Complete First").IsChecked = true; Layout();
        Check(store.Load().Entries.First(x => x.Title == "First").Done, "The displayed saved-row checkbox immediately persists completion through its Checked handler");
        CheckVisibleDot("First", "The saved-row Checked handler displays the filled completion dot");
        Pump(300);
        Check(Named<CheckBox>("Restore First") == null && store.Load().Entries.First(x => x.Title == "First").Done, "The saved-row checkbox feedback disappears while completion remains saved");

        Reset(); window.Edit(Task("Watch"), NoteStore.ReadingGroup); Layout();
        Named<TextBox>("Item text").Text = "Watch renamed";
        Named<TextBox>("Item link").Text = "https://example.test/edited-link";
        var inlineCheck = Named<CheckBox>("Complete current item");
        // A CheckBox click toggles IsChecked before raising Click. The inline
        // row deliberately subscribes to Click, not Checked, to commit drafts.
        inlineCheck.IsChecked = true;
        inlineCheck.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Layout();
        var saved = store.Load().Entries.FirstOrDefault(x => x.Title == "Watch renamed");
        Check(saved != null && saved.Done && saved.Link == "https://example.test/edited-link" && !store.Load().Entries.Any(x => x.Title == "Watch"), "The inline checkbox Click handler saves the edited name and link together with completion");
        Check(Field("inline") == null, "Completing from an inline checkbox leaves no hidden editing draft");
        CheckVisibleDot("Watch renamed", "The inline checkbox Click handler displays the renamed card's filled completion dot");
        Pump(300);
        saved = store.Load().Entries.First(x => x.Title == "Watch renamed");
        Check(Named<CheckBox>("Restore Watch renamed") == null && saved.Done && saved.Link == "https://example.test/edited-link", "The inline checkbox feedback disappears while edited name, link and completion stay saved");
    }
    static void RunGestureCases() {
        Reset(); string firstTint = Tint(Card(Named<CheckBox>("Complete First")));
        Complete(Task("First"));
        var second = Named<CheckBox>("Complete Second"); var secondCard = Card(second);
        var divider = Named<FrameworkElement>("Resize Watching list");
        var pane = Named<ScrollViewer>("Items in Plans"); var before = File.ReadAllText(store.CurrentPath);
        SetField("watchingResizeActive", true);
        try {
            Pump(300);
            Check(ReferenceEquals(second, Named<CheckBox>("Complete Second")) && ReferenceEquals(secondCard, Card(second)), "Feedback expiry preserves saved-row controls during divider dragging with no active draft");
            Check(ReferenceEquals(divider, Named<FrameworkElement>("Resize Watching list")) && ReferenceEquals(pane, Named<ScrollViewer>("Items in Plans")), "Feedback expiry preserves the captured divider and column scroll container");
            Check(Tint(secondCard) == firstTint && Named<Button>("Edit empty item 2 in Plans") != null, "Surgical removal without an editor updates gradients and blank-row positions");
            Check(File.ReadAllText(store.CurrentPath) == before && Named<CheckBox>("Restore First") == null, "Feedback expiry during a gesture only removes the completed visual");
        } finally { SetField("watchingResizeActive", false); }
        Reset(); Complete(Task("Watch"));
        pane = Named<ScrollViewer>("Items in Watching list"); var add = Named<Button>("Add to Watching list");
        root.Visibility = Visibility.Hidden;
        try {
            Pump(300);
            Check(ReferenceEquals(pane, Named<ScrollViewer>("Items in Watching list")) && ReferenceEquals(add, Named<Button>("Add to Watching list")), "Hidden-board completion leaves existing headings and scroll containers intact");
            Check(!Descendants(pane).OfType<Border>().Any(x => x.Background is LinearGradientBrush), "Completing the final card leaves an empty usable column without rebuilding it");
        } finally { root.Visibility = Visibility.Visible; Layout(); }
    }
    static void Run() {
        RunGestureCases();
        RunCheckboxPaths();
        Reset(); string before = File.ReadAllText(store.CurrentPath);
        Complete(Task("First"));
        Check(File.ReadAllText(store.CurrentPath).Contains("- [x] First"), "Completion is durable before its visual feedback ends");
        var check = Named<CheckBox>("Restore First");
        var dot = check == null ? null : check.Template.FindName("CompletionDot", check) as Ellipse;
        var ring = check == null ? null : check.Template.FindName("RingShape", check) as Ellipse;
        Check(check != null && check.IsChecked == true && dot != null && dot.Visibility == Visibility.Visible && ring != null && dot.Width < ring.Width - ring.StrokeThickness * 2, "Completed feedback renders an inner dot separated from its circular outline");
        Pump(300);
        Check(Named<CheckBox>("Restore First") == null && File.ReadAllText(store.CurrentPath).Contains("- [x] First"), "The feedback card disappears without another data write");
        Call("Undo"); Layout();
        Check(File.ReadAllText(store.CurrentPath) == before && Named<CheckBox>("Complete First") != null, "Undo after disappearance restores the original row");

        Reset(); Complete(Task("First")); Call("Undo"); Layout(); Pump(300);
        Check(File.ReadAllText(store.CurrentPath) == Fixture && Named<CheckBox>("Complete First") != null, "Immediate Undo cancels completion appearance without later removing the restored card");

        Reset(); string firstTint = Tint(Card(Named<CheckBox>("Complete First")));
        Complete(Task("First")); window.Edit(Task("Second"), "Plans"); Layout(); Pump(1);
        object draft = Field("inline"); var input = Named<TextBox>("Item text");
        input.Text = "Unsaved draft"; input.Select(2, 3);
        string saved = File.ReadAllText(store.CurrentPath); Pump(300);
        Check(ReferenceEquals(draft, Field("inline")) && ReferenceEquals(input, Named<TextBox>("Item text")) && input.Text == "Unsaved draft" && input.SelectionStart == 2 && input.SelectionLength == 3, "Feedback expiry preserves the active TextBox, draft and selection");
        Check(File.ReadAllText(store.CurrentPath) == saved && saved.Contains("- [ ] Second"), "Feedback expiry never commits a neighboring draft");
        Check(Named<CheckBox>("Restore First") == null && Tint(Card(input)) == firstTint, "The completed sibling disappears and the active card adopts its new gradient position");
        Check(Named<Button>("Edit empty item 2 in Plans") != null && Named<Button>("Edit empty item 3 in Plans") == null, "Blank-card accessible positions update without rebuilding the draft");

        Reset(); Complete(Task("First")); window.Edit(Document.Entries.First(x => x.Title == "First"), "Plans");
        Check(Field("inline") == null, "Transient completed cards cannot become invisible inline editors");
        Pump(300);

        Reset(Fixture.Replace("- [ ] First", "- [ ] Duplicate").Replace("- [ ] Second", "- [ ] Duplicate"));
        Complete(Task("Duplicate")); Complete(Task("Duplicate"));
        Check(Descendants(root).OfType<CheckBox>().Count(x => AutomationProperties.GetName(x) == "Restore Duplicate") == 2, "Rapid completions keep independent feedback for duplicate titles");
        Pump(300);
        Check(Named<CheckBox>("Restore Duplicate") == null && store.Load().Entries.Count(x => x.Title == "Duplicate" && x.Done) == 2, "Both duplicate completions disappear and remain durably completed");

        Reset(); Complete(Task("First"));
        string external = File.ReadAllText(store.CurrentPath).Replace("- [x] First\n", "").Replace("Second", "Changed elsewhere");
        File.WriteAllText(store.CurrentPath, external); window.Refresh(); Layout(); Pump(300);
        Check(File.ReadAllText(store.CurrentPath) == external && Named<CheckBox>("Restore First") == null && Named<CheckBox>("Complete Changed elsewhere") != null, "An external edit during feedback is preserved and no completed row resurfaces");

        Reset(); Complete(Task("Watch"));
        var menu = (ContextMenu)Call("MoreMenu");
        string[] removed = { "Show completed", "Open notes folder", "Edit in Obsidian" };
        Check(!menu.Items.OfType<MenuItem>().Any(x => removed.Contains(x.Header as string)), "The removed menu actions are absent");
        window.Exit();
        Check(store.Load().Entries.First(x => x.Title == "Watch").Done, "Quitting during feedback retains the completed Watching card on disk");
        var timer = (DispatcherTimer)Field("completionTimer");
        Check(!timer.IsEnabled, "Closing the window stops its feedback timer");
    }
    [STAThread] static int Main(string[] args) {
        Application app = null;
        try {
            string output = System.IO.Path.GetFullPath(args[0]);
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            store = new NoteStore(System.IO.Path.Combine(output, "vault"), System.IO.Path.Combine(output, "backups")); store.Initialize();
            window = new MainWindow(store, new Settings { Width = 1000, Height = 600, Pinned = false }, output);
            root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
            if (args.Length > 1 && args[1] == "checkbox") RunCheckboxPaths(); else Run();
            Console.WriteLine(checks + " completion feedback checks passed without showing a window."); return 0;
        } catch (Exception ex) {
            for (; ex != null; ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        } finally { if (window != null && !window.Dispatcher.HasShutdownStarted) { SetField("inline", null); window.Close(); } if (app != null) app.Shutdown(); }
    }
}
