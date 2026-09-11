using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shike;

// The real preparation, geometry, rendering and save paths without a HWND.
static class ItemReorderingTests {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Fixture = "# Test\n\n## Watching list\n- [ ] [Watch A](<https://example.test/a>)\n  > Keep this note\n- [ ] [Watch B](<https://example.test/b>)\n\n## Plans\n- [ ] A\n- [ ] B\n- [ ] C\n- [ ] D\n- [ ] E\n- [ ] F\n- [ ] G\n\n## Other\n- [ ] Other A\n";
    static MainWindow window;
    static FrameworkElement root;
    static NoteStore store;
    static int checks;
    static object Field(string name) { return typeof(MainWindow).GetField(name, Hidden).GetValue(window); }
    static void SetField(string name, object value) { typeof(MainWindow).GetField(name, Hidden).SetValue(window, value); }
    static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, Hidden).Invoke(window, args); }
    static object Static(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
    static object Session { get { return Field("itemReorder"); } }
    static object Part(string name) { return Session.GetType().GetField(name).GetValue(Session); }
    static NoteDocument Document { get { return (NoteDocument)Field("document"); } }
    static Grid Groups { get { return (Grid)Field("groups"); } }
    static string FileText { get { return File.ReadAllText(store.CurrentPath); } }
    static Entry Entry(string title) { return Document.Entries.First(x => x.Title == title); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject node) {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    static T Named<T>(string name) where T : DependencyObject { return Descendants(root).OfType<T>().First(x => AutomationProperties.GetName(x) == name); }
    static Border Card(Entry item) {
        var property = (DependencyProperty)typeof(MainWindow).GetField("ReorderEntryProperty", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return Descendants(root).OfType<Border>().First(x => ReferenceEquals(x.GetValue(property), item));
    }
    static void Layout() { root.Measure(new Size(1000, 650)); root.Arrange(new Rect(0, 0, 1000, 650)); root.UpdateLayout(); }
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Reset(string text = Fixture) {
        Call("CancelItemReordering"); SetField("inline", null); SetField("undoBefore", null); SetField("undoAfter", null);
        File.WriteAllText(store.CurrentPath, text); window.Refresh(); Layout();
        foreach (var pane in Descendants(root).OfType<ScrollViewer>()) { pane.ScrollToVerticalOffset(0); pane.ScrollToHorizontalOffset(0); } Layout();
    }
    static void Prepare(Entry item) {
        var card = Card(item); var session = Activator.CreateInstance(typeof(MainWindow).GetNestedType("ItemReorderSession", BindingFlags.NonPublic));
        session.GetType().GetField("Item").SetValue(session, item); session.GetType().GetField("Card").SetValue(session, card);
        session.GetType().GetField("PressedAt").SetValue(session, card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2), Groups));
        SetField("itemReorder", session);
        Check((bool)Call("PrepareItemReordering"), "Preparing a drag resolves its current saved card"); Layout();
    }
    static string Tint(Entry item) { return string.Join("|", ((LinearGradientBrush)Card(item).Background).GradientStops.Select(x => x.Color.ToString())); }
    static string TintRgb(Brush brush) { return string.Join("|", ((GradientBrush)brush).GradientStops.Select(x => x.Color.R + "," + x.Color.G + "," + x.Color.B)); }
    static bool Opaque(Border card) { var brush = card.Background as GradientBrush; return card.Opacity == 1 && brush != null && brush.Opacity == 1 && brush.GradientStops.All(x => x.Color.A == 255); }
    static Point InList(double x, double y) { return ((ScrollViewer)Part("List")).TranslatePoint(new Point(x, y), Groups); }
    static void UpdateBefore(Entry item) { var card = Card(item); Call("UpdateItemReordering", card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 4), Groups)); }
    static bool Drop() {
        var item = (Entry)Part("Item"); var before = (Entry)Part("Before"); bool valid = (bool)Part("CanDrop");
        Call("CancelItemReordering"); bool moved = valid && (bool)Call("MoveItemBefore", item, before); Layout(); return moved;
    }
    static void Pump(int ms) {
        var frame = new DispatcherFrame(); var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        stop.Tick += delegate { stop.Stop(); frame.Continue = false; }; stop.Start(); Dispatcher.PushFrame(frame); Layout();
    }
    static void MovementAndPaint() {
        Reset(); string original = FileText;
        string[] shades = new[] { "A", "B", "C", "D", "E", "F", "G" }.Select(x => Tint(Entry(x))).ToArray();
        var item = Entry("E"); Prepare(item); UpdateBefore(Entry("B"));
        Check((bool)Part("CanDrop") && ReferenceEquals(Part("Before"), Entry("B")), "Upper-half hit testing selects the live insertion anchor three rows earlier");
        Check(Drop() && Document.Entries.Where(x => x.Group == "Plans").Select(x => x.Title).SequenceEqual(new[] { "A", "E", "B", "C", "D", "F", "G" }), "Dropping three places upward saves the exact new order");
        Check(new[] { "A", "E", "B", "C", "D", "F", "G" }.Select(x => Tint(Entry(x))).SequenceEqual(shades), "All rendered card gradients follow their new positions after moving upward");
        Call("Undo"); Layout(); Check(FileText == original, "Reorder Undo restores the exact original Markdown");
        Prepare(Entry("B")); UpdateBefore(Entry("F")); Check(Drop(), "A card can move three places downward");
        Check(Document.Entries.Where(x => x.Group == "Plans").Select(x => x.Title).SequenceEqual(new[] { "A", "C", "D", "E", "B", "F", "G" }), "Downward moves account for removal before insertion");
        Check(new[] { "A", "C", "D", "E", "B", "F", "G" }.Select(x => Tint(Entry(x))).SequenceEqual(shades), "Rendered shades also follow position after moving downward");
        Call("Undo"); Layout();
        Prepare(Entry("Watch B")); UpdateBefore(Entry("Watch A")); Check(Drop(), "Watching cards use the same within-column move interaction");
        Check(Document.Entries.First(x => x.Group == NoteStore.ReadingGroup).Title == "Watch B" && Entry("Watch A").Note == "Keep this note" && Entry("Watch A").Link == "https://example.test/a", "Watching titles, links and note spans stay attached to their cards");
    }
    static void CancellationAndEditing() {
        Reset(); var item = Entry("E"); var card = Card(item); var transform = card.RenderTransform; var background = card.Background; double opacity = card.Opacity; string text = FileText; int children = Groups.Children.Count;
        Prepare(item); UpdateBefore(Entry("B"));
        Check(Opaque(card) && TintRgb(card.Background) == TintRgb(background), "A lifted card fully covers underlying text while preserving its gradient colors");
        Call("CancelItemReordering");
        Check(ReferenceEquals(Card(item), card) && ReferenceEquals(card.RenderTransform, transform) && ReferenceEquals(card.Background, background) && card.Opacity == opacity && Groups.Children.Count == children && FileText == text, "Cancel restores the original card and glass brush without rendering or writing");
        Prepare(item); var other = Named<ScrollViewer>("Items in Other"); Call("UpdateItemReordering", other.TranslatePoint(new Point(30, 30), Groups));
        Check(!(bool)Part("CanDrop") && ((Border)Part("Line")).Visibility == Visibility.Collapsed && !Drop() && FileText == text, "Cross-column movement hides the marker and cancels the drop");
        window.Edit(Entry("A"), "Plans"); Layout(); Named<TextBox>("Item text").Text = "Edited A";
        Prepare(Entry("E")); UpdateBefore(Entry("F"));
        Check(!Drop() && store.Load().Entries.Any(x => x.Title == "Edited A"), "A no-op drag still saves the other active draft once");
        Call("Undo"); Layout(); Check(FileText == Fixture, "A no-op drag preserves that previous text edit's Undo");
        Reset(); item = Entry("E"); window.Edit(item, "Plans"); Layout(); Named<TextBox>("Item text").Text = "Edited source";
        Prepare(item); Check(((Entry)Part("Item")).Title == "Edited source" && ReferenceEquals(Card((Entry)Part("Item")), Part("Card")), "Dragging an edited source rebinds to its newly saved identity"); Call("CancelItemReordering");
        Reset(); item = Entry("Watch B"); window.Edit(item, NoteStore.ReadingGroup); Layout(); Named<TextBox>("Item text").Text = "Edited watch"; Named<TextBox>("Item link").Text = "https://example.test/new-link";
        Prepare(item); Check(((Entry)Part("Item")).Title == "Edited watch" && ((Entry)Part("Item")).Link == "https://example.test/new-link", "Source preparation saves both independently edited Watching fields"); Call("CancelItemReordering");
        Reset(); item = Entry("E"); Call("BeginTopic", "Plans"); Layout(); Named<TextBox>("Topic name").Text = "Work";
        Prepare(item); Check(((Entry)Part("Item")).Group == "Work" && store.Load().Groups.Contains("Work"), "Dragging a card while renaming its topic follows the saved group identity"); Call("CancelItemReordering");
        Reset(); item = Entry("E"); window.Edit(item, "Plans"); Layout(); Named<TextBox>("Item text").Text = "Unsaved";
        File.WriteAllText(store.CurrentPath, FileText.Replace("- [ ] E", "- [ ] External E"));
        var session = Activator.CreateInstance(typeof(MainWindow).GetNestedType("ItemReorderSession", BindingFlags.NonPublic)); session.GetType().GetField("Item").SetValue(session, item); session.GetType().GetField("Card").SetValue(session, Card(item)); SetField("itemReorder", session);
        Check(!(bool)Call("PrepareItemReordering") && Field("inline") != null && Named<TextBox>("Item text").Text == "Unsaved" && FileText.Contains("External E"), "A conflicting draft blocks dragging without losing either edit");
    }
    static void SurfaceAndScrolling() {
        Reset(); var item = Entry("Watch A"); var card = Card(item);
        Check((bool)Static("ItemDragSurface", Named<Button>("Edit Watch A"), card) && (bool)Static("ItemDragSurface", Named<Button>("Edit link Watch A"), card), "Saved title and link buttons are eligible for threshold dragging");
        Check(!(bool)Static("ItemDragSurface", Named<CheckBox>("Complete Watch A"), card) && !(bool)Static("ItemDragSurface", Named<Button>("Open Watch A"), card), "Completion circles and link arrows keep their own actions");
        window.Edit(item, item.Group); Layout(); Check(!(bool)Static("ItemDragSurface", Named<TextBox>("Item text"), Card(item)), "An active text editor retains native selection gestures");
        Check(!(bool)Static("PassedItemDragThreshold", new Point(), new Point(SystemParameters.MinimumHorizontalDragDistance / 2, SystemParameters.MinimumVerticalDragDistance / 2)) && (bool)Static("PassedItemDragThreshold", new Point(), new Point(0, SystemParameters.MinimumVerticalDragDistance)), "A normal click stays below the native drag threshold");
        string many = "# Test\n\n## Watching list\n" + string.Join("", Enumerable.Range(0, 30).Select(i => "- [ ] W" + i + "\n")) + "\n## Plans\n" + string.Join("", Enumerable.Range(0, 30).Select(i => "- [ ] P" + i + "\n")) + "\n## Other\n- [ ] Other A\n\n## Extra\n- [ ] Extra A\n";
        Reset(many); Prepare(Entry("P3")); var list = (ScrollViewer)Part("List"); var watching = Named<ScrollViewer>("Items in Watching list");
        Check(list.ScrollableHeight > 0, "Autoscroll fixture has actual overflowing rows");
        Call("AutoScrollItemReordering", InList(30, list.ViewportHeight - 2), .05); Layout();
        Check(list.VerticalOffset > 0 && watching.VerticalOffset == 0, "Bottom-edge autoscroll moves only the source column");
        double offset = list.VerticalOffset; Call("AutoScrollItemReordering", InList(30, 2), .05); Layout();
        Check(list.VerticalOffset < offset, "Top-edge autoscroll reverses smoothly in the same column");
        var strip = (ScrollViewer)Field("scroll"); strip.ScrollToHorizontalOffset(120); Layout();
        Point overWatching = watching.TranslatePoint(new Point(watching.ViewportWidth - 8, list.ViewportHeight - 2), Groups);
        offset = list.VerticalOffset; Call("AutoScrollItemReordering", overWatching, .05); Call("UpdateItemReordering", overWatching); Layout();
        Check(!(bool)Part("CanDrop") && list.VerticalOffset == offset, "A partly clipped topic cannot accept or autoscroll over Watching");
        Rect visible = (Rect)Call("ItemReorderBounds", Session);
        Call("UpdateItemReordering", new Point(visible.Left + Math.Min(20, visible.Width / 2), visible.Top + 60));
        var marker = (Border)Part("Line");
        Check((bool)Part("CanDrop") && Canvas.GetLeft(marker) >= visible.Left && Canvas.GetLeft(marker) + marker.Width <= visible.Right, "Insertion feedback stays inside the actually visible topic slice");
        Call("CancelItemReordering");
        Reset(); string movedShade = Tint(Entry("D")), movedRgb = TintRgb(Card(Entry("D")).Background);
        var completed = Entry("A"); var next = new Entry { Group = completed.Group, Title = completed.Title, Link = completed.Link, Note = completed.Note, Done = true }; Call("Change", completed, next, "Plans", "Completed"); Layout();
        Prepare(Entry("E")); var sourceCard = (Border)Part("Card"); Pump(250);
        Check(Opaque(sourceCard) && TintRgb(sourceCard.Background) == movedRgb, "Completion expiry immediately updates the lifted shade without making it translucent");
        UpdateBefore(Entry("B"));
        Check(ReferenceEquals(sourceCard, Part("Card")) && ReferenceEquals(Part("Before"), Entry("B")) && (bool)Part("CanDrop"), "Completion expiry during dragging leaves the source and live insertion target valid"); Call("CancelItemReordering");
        Check(Tint(Entry("E")) == movedShade && !Opaque(sourceCard), "Cancelling after a sibling expires restores the new row shade and original glass transparency");
        var replacement = new SolidColorBrush(Colors.Teal); sourceCard.Background = replacement;
        Check(ReferenceEquals(sourceCard.Background, replacement), "Ending a drag removes its background observer");
    }
    [STAThread] static int Main(string[] args) {
        Application app = null;
        try {
            string state = Path.GetFullPath(args[0]); Directory.CreateDirectory(state); app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            store = new NoteStore(Path.Combine(state, "vault"), Path.Combine(state, "backups")); store.Initialize();
            window = new MainWindow(store, new Settings { Width = 1000, Height = 650 }, state); root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
            MovementAndPaint(); CancellationAndEditing(); SurfaceAndScrolling();
            Console.WriteLine(checks + " item reorder checks passed without showing a window."); return 0;
        } catch (Exception ex) { for (int i = 0; ex != null && i < 6; i++, ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1; }
        finally { if (window != null) { Call("CancelItemReordering"); SetField("inline", null); window.Exit(); } if (app != null) app.Shutdown(); }
    }
}
