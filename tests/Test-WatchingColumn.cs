using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shike;

// Actual inline actions and Thumb routed events; no visible HWND is created.
static class WatchingColumnTests {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Fixture = "# Test\n\n## Watching list\n- [ ] [Watch](<https://example.test/watch>)\n  > Keep metadata.\n\n## Plans\n- [ ] Plan\n- [ ] Next\n";
    static MainWindow window;
    static FrameworkElement root;
    static NoteStore store;
    static Settings settings;
    static string output;
    static int checks;
    static double renderedWidth = 1000;
    static object Field(string name) { return typeof(MainWindow).GetField(name, Hidden).GetValue(window); }
    static void SetField(string name, object value) { typeof(MainWindow).GetField(name, Hidden).SetValue(window, value); }
    static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, Hidden).Invoke(window, args); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject value) {
        yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(value, i))) yield return child;
    }
    static T Named<T>(string name) where T : DependencyObject { return Descendants(root).OfType<T>().FirstOrDefault(x => AutomationProperties.GetName(x) == name); }
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Layout(double width = -1) { if (width > 0) renderedWidth = width; root.Measure(new Size(renderedWidth, 600)); root.Arrange(new Rect(0, 0, renderedWidth, 600)); root.UpdateLayout(); root.UpdateLayout(); }
    static string Text { get { return File.ReadAllText(store.CurrentPath); } }
    static TextBox Input { get { return Named<TextBox>("Topic name"); } }
    static string Label { get { return ((TextBox)Named<Button>("Rename Watching list").Content).Text; } }
    static double WatchingWidth { get { return ((Grid)Field("groups")).ColumnDefinitions[0].Width.Value; } }
    static void Open() {
        window = new MainWindow(store, settings, output);
        root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources(); Layout();
    }
    static void Rename(string title, bool advance = false) {
        Named<Button>("Rename Watching list").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Check(Input != null, "The Watching title opens the shared inline editor");
        Input.Text = title;
        Check((bool)Call("FinishInline", advance), "The Watching title edit commits"); Layout();
    }
    static void StartDrag() { Named<Thumb>("Resize Watching list").RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent }); Layout(); }
    static void Drag(double amount) { Named<Thumb>("Resize Watching list").RaiseEvent(new DragDeltaEventArgs(amount, 0) { RoutedEvent = Thumb.DragDeltaEvent }); Layout(); }
    static void EndDrag(bool cancelled = false) { Named<Thumb>("Resize Watching list").RaiseEvent(new DragCompletedEventArgs(0, 0, cancelled) { RoutedEvent = Thumb.DragCompletedEvent }); Layout(); }
    static void CheckUsablePanes(string label) {
        double available = ((Grid)Field("groups")).ActualWidth;
        double minimum = Math.Min(100, Math.Max(0, available - 26) / 2);
        double topic = ((ScrollViewer)Field("scroll")).ViewportWidth;
        Check(WatchingWidth >= minimum - 1 && topic >= minimum - 1, label + ": both panes retain usable space (Watching " + WatchingWidth.ToString("0.#") + ", topics " + topic.ToString("0.#") + " DIP)");
    }
    static void Run() {
        string legacy = Path.Combine(output, "legacy"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"DesignVersion\":2}");
        var old = Settings.Load(legacy);
        Check(old.WatchingTitle == NoteStore.ReadingGroup && old.WatchingWidth == 0, "Existing settings retain the original Watching title and automatic width");
        Check(Label == NoteStore.ReadingGroup && settings.WatchingWidth == 0, "Initial layout keeps the existing Watching title and sizing");
        Check(Named<Thumb>("Resize Watching list").ActualWidth >= 20 && Named<Border>("Watching list divider").ActualWidth == 1, "The divider has a broad hit target around the original one-DIP visual");

        string before = Text;
        Rename("For later");
        Check(Label == "For later" && Settings.Load(output).WatchingTitle == "For later" && Text == before, "Renaming the Watching title persists its label without rewriting the note");
        Check(store.Load().Groups.Contains(NoteStore.ReadingGroup) && !store.Load().Groups.Contains("For later"), "The Watching group keeps its stable internal identity");
        Call("Undo"); Layout();
        Check(Label == NoteStore.ReadingGroup && Settings.Load(output).WatchingTitle == NoteStore.ReadingGroup && Text == before, "Undo restores the previous Watching title without changing tasks");
        Rename("");
        Check(Label == "" && Settings.Load(output).WatchingTitle == "" && Text == before, "A cleared Watching title stays empty and has no placeholder");
        window.Exit(); settings = Settings.Load(output); Open();
        Check(Label == "" && Text == before, "An empty Watching title survives closing and reopening");
        Named<Button>("Rename Watching list").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Input.Text = "Cancelled label"; Call("CancelInline"); Layout();
        Check(Label == "" && Settings.Load(output).WatchingTitle == "", "Escape cancels a Watching title draft");

        Rename("Watch carefully", true);
        Check(settings.WatchingTitle == "Watch carefully" && Named<TextBox>("Item text") != null && Named<TextBox>("Item link") != null, "Enter on the Watching title opens a real inline name-and-link card");
        Check(store.Load().Entries.Count(x => x.Group == NoteStore.ReadingGroup) == 2 && store.Load().Entries.Last(x => x.Group == NoteStore.ReadingGroup).Title == "", "Title Enter inserts exactly one durable blank Watching card");
        Call("Undo"); Layout();
        Check(settings.WatchingTitle == "" && Text == before && Named<TextBox>("Item text") == null, "One Undo reverses the title change and its new blank together");

        Rename("For later");
        window.Edit(store.Load().Entries.First(x => x.Title == "Plan"), "Plans"); Layout();
        Named<TextBox>("Item text").Text = "Plan edited";
        Check((bool)Call("FinishInline", false), "A later ordinary task edit commits"); Layout(); Call("Undo"); Layout();
        Check(settings.WatchingTitle == "For later" && Text == before, "Undo of a later task edit does not undo an older Watching rename");

        Named<Button>("Rename Watching list").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); Input.Text = "Saved before resizing";
        double initial = WatchingWidth;
        StartDrag();
        Check(Input == null && settings.WatchingTitle == "Saved before resizing", "Starting a divider drag commits the active title once");
        var card = Named<Button>("Edit Plan"); var watching = Named<Border>("Column Watching list");
        string afterStart = Text;
        for (int i = 0; i < 5; i++) Drag(0.2);
        Check(Math.Abs(settings.WatchingWidth - initial - 1) < 0.001, "Sub-DIP drag deltas accumulate without sticky rounding");
        Drag(40);
        Check(ReferenceEquals(card, Named<Button>("Edit Plan")) && ReferenceEquals(watching, Named<Border>("Column Watching list")) && Text == afterStart, "Divider drag updates width without rebuilding cards or changing notes");
        Check(Math.Abs(WatchingWidth - initial - 41) < 0.001, "Dragging right widens the Watching column by the drag distance");
        Check(Settings.Load(output).WatchingWidth == 0, "Dragging does not write width preferences on every movement");
        EndDrag();
        double preferred = settings.WatchingWidth;
        Check(Math.Abs(Settings.Load(output).WatchingWidth - preferred) < 0.001, "Releasing the divider persists its preferred width");
        Layout(360);
        Check(WatchingWidth <= preferred && settings.WatchingWidth == preferred, "A narrow window clamps the rendered width without losing the preference");
        CheckUsablePanes("Narrow window with a saved wide Watching preference");
        Layout(1000);
        Check(Math.Abs(WatchingWidth - preferred) < 0.001, "Enlarging the window restores the preferred Watching width");
        foreach (double zoom in new[] { .8, 1.5 }) {
            settings.Zoom = zoom; Call("ApplyZoom"); Layout(340);
            CheckUsablePanes("Minimum window at zoom " + zoom);
            StartDrag(); Drag(100000); EndDrag();
            CheckUsablePanes("Extreme right divider drag at zoom " + zoom);
            StartDrag(); Drag(-100000); EndDrag();
            CheckUsablePanes("Extreme left divider drag at zoom " + zoom);
        }
        settings.Zoom = 1; Call("ApplyZoom"); Layout(1000);
        StartDrag(); Drag(100000); EndDrag();
        double available = ((Grid)Field("groups")).ActualWidth;
        Check(WatchingWidth <= available - 26 - 210 + 0.01, "The wide clamp leaves room for the adjacent topics");
        StartDrag(); Drag(-100000); EndDrag();
        Check(WatchingWidth == 170, "The narrow clamp preserves a usable Watching column");
        preferred = settings.WatchingWidth;
        StartDrag(); Drag(60); EndDrag(true);
        Check(settings.WatchingWidth == preferred && Settings.Load(output).WatchingWidth == preferred, "Cancelling a divider gesture restores its previous width");
        window.Exit(); settings = Settings.Load(output); Open();
        Check(settings.WatchingTitle == "Saved before resizing" && WatchingWidth == preferred && Text == before, "Watching title and divider preference survive reopening without modifying tasks");
    }
    [STAThread] static int Main(string[] args) {
        Application app = null;
        try {
            output = Path.GetFullPath(args[0]); app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            store = new NoteStore(Path.Combine(output, "vault"), Path.Combine(output, "backups")); store.Initialize(); File.WriteAllText(store.CurrentPath, Fixture);
            settings = new Settings { Width = 1000, Height = 600, Pinned = false, DesignVersion = 2 };
            Open(); Run(); Console.WriteLine(checks + " Watching column checks passed without showing a window."); return 0;
        } catch (Exception ex) { for (; ex != null; ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1; }
        finally { if (window != null) { SetField("inline", null); window.Close(); } if (app != null) app.Shutdown(); }
    }
}
