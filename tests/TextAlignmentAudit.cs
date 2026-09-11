using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Shike;

partial class TextAlignmentTests {
    static void TypographyBoard() {
        AuditConfigure(1120,1,240); Theme.SetDark(true);
        ((Panel)root).Background=new SolidColorBrush(Color.FromRgb(22,25,31));
        var samples=new[]{"关于","Project notes","Review the design","阅读计划","Éléphant Ångström","A longer task that wraps while keeping the text comfortably spaced","Follow up","Archive references"};
        string fixture="# Typography preview fixture\n";
        foreach(string group in new[]{NoteStore.ReadingGroup,"Study","Research"}) {
            fixture+="\n## "+group+"\n";
            for(int index=0;index<samples.Length;index++) fixture+=NoteDocument.Serialize(new Entry {Group=group,Title=samples[index],Link=group==NoteStore.ReadingGroup?"https://example.test/reading-"+(index+1):"",Note=index==2?"Supporting note":""},"\n")+"\n";
        }
        File.WriteAllText(store.CurrentPath,fixture,new System.Text.UTF8Encoding(false));window.Refresh();Layout();
        Save(RenderRoot(),Path.Combine(output,"typography-board-200.png"));
        foreach(var button in Descendants(root).OfType<Button>().Where(x=>AutomationProperties.GetName(x).StartsWith("Edit "))) foreach(var box in Descendants(button).OfType<TextBox>()) AuditCheck(box.FontWeight==FontWeights.Normal,"Displayed item body is regular: "+box.Text);
        foreach(var button in Descendants(root).OfType<Button>().Where(x=>AutomationProperties.GetName(x).StartsWith("Rename "))) foreach(var box in Descendants(button).OfType<TextBox>()) AuditCheck(box.FontWeight==FontWeights.Bold,"Heading stays bold: "+box.Text);
        foreach(var note in Descendants(root).OfType<TextBlock>().Where(x=>x.Text=="Supporting note")) AuditCheck(note.FontWeight==FontWeights.Normal,"Supporting note is regular");
        Console.WriteLine(auditChecks+" board typography checks; "+failures+" failures. Preview uses isolated fixture data.");
    }
    static int auditChecks;
    static readonly List<string> layoutReport = new List<string> { "window_width,zoom,watching_preference,watching_width,topic_viewport,topic_horizontal_extent,topic_screen_left,topic_screen_right" };
    static void AuditCheck(bool condition, string message) {
        auditChecks++; if (!condition) failures++;
        Console.WriteLine((condition ? "PASS " : "FAIL ") + message);
    }
    static void AuditConfigure(double width, double zoom, double watching) {
        typeof(MainWindow).GetField("inline", Private).SetValue(window, null);
        layoutWidth = width; layoutHeight = 640; window.Width = width; window.Height = layoutHeight;
        var config = (Settings)Field("config"); config.Zoom = zoom;
        typeof(Settings).GetField("WatchingWidth").SetValue(config, watching);
        typeof(Settings).GetField("WatchingTitle").SetValue(config, NoteStore.ReadingGroup);
        Call("ApplyZoom"); dpi = 2; VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi, dpi));
        scenarioSuffix = "-width" + N(width) + "-watch" + N(watching);
    }
    static void Fixture(params Entry[] entries) {
        typeof(MainWindow).GetField("inline", Private).SetValue(window, null);
        string text = "# Audit\n";
        foreach (string group in new[] { NoteStore.ReadingGroup, "Plans" }) {
            text += "\n## " + group + "\n";
            foreach (var entry in entries.Where(x => x.Group == group)) text += NoteDocument.Serialize(entry, "\n") + "\n";
        }
        File.WriteAllText(store.CurrentPath, text, new System.Text.UTF8Encoding(false)); window.Refresh(); Layout();
    }
    static Entry EntryByTitle(string text) { return ((NoteDocument)Field("document")).Entries.First(x => x.Title == text); }
    static Rect CardByTitle(string title) { return Bounds(Host(Display("Edit " + title, title), false)); }
    static Dictionary<string, ScrollViewer> Scrolls() { return (Dictionary<string, ScrollViewer>)Field("columnScrolls"); }
    static void AuditLayout(double width, double zoom, double watching) {
        AuditConfigure(width, zoom, watching);
        Fixture(new Entry { Group = NoteStore.ReadingGroup, Title = "Reference", Link = "https://example.test" }, new Entry { Group = "Plans", Title = "Task" });
        var strip = (ScrollViewer)Field("scroll"); var groups = (Grid)Field("groups"); Rect bounds = Bounds(strip);
        layoutReport.Add(string.Join(",", new[] { width, zoom, watching, groups.ColumnDefinitions[0].Width.Value, strip.ViewportWidth, strip.ExtentWidth, bounds.Left, bounds.Right }.Select(N)));
        Save(RenderRoot(), Path.Combine(output, "layout" + scenarioSuffix + "-zoom" + N(zoom) + ".png"));
        double usable = Math.Min(100, Math.Max(0, groups.ActualWidth - 26) / 2);
        AuditCheck(strip.ViewportWidth >= usable - 1 && bounds.Left < layoutWidth, "Topics retain usable scrolling space" + scenarioSuffix + " zoom" + N(zoom) + " (" + N(strip.ViewportWidth) + " DIP; required " + N(usable) + ")");
        var more = (Button)Field("more"); var collapse = (Button)Field("collapse"); var close = (Button)Field("close");
        var controls = new[] { more, collapse, close }.Select(Bounds).ToArray();
        AuditCheck(controls.All(x => Math.Abs(x.Top + x.Height / 2 - (controls[0].Top + controls[0].Height / 2)) < .01) && controls[2].Right <= layoutWidth, "Header actions remain centered and inside width " + N(width) + " at zoom " + N(zoom));
    }
    static void AuditTransition(string group, double width, double zoom, double watching) {
        AuditConfigure(width, zoom, watching);
        string other = group == "Plans" ? NoteStore.ReadingGroup : "Plans";
        Fixture(new Entry { Group = group, Title = "Before" }, new Entry { Group = group, Title = "" }, new Entry { Group = group, Title = "After" }, new Entry { Group = other, Title = "Other column" });
        Rect before = CardByTitle("Before"), after = CardByTitle("After"), otherBefore = CardByTitle("Other column");
        var blank = ((NoteDocument)Field("document")).Entries.First(x => x.Group == group && x.Title.Length == 0);
        window.Edit(blank, group); Layout(); var input = (TextBox)DraftField("Input"); Rect empty = Bounds(Host(input, false));
        input.Text = "A detailed task that grows into several wrapped lines while keeping the neighbouring cards in their intended positions"; Layout();
        Rect grown = Bounds(Host(input, false)); Rect previousNow = CardByTitle("Before"), nextNow = CardByTitle("After");
        AuditCheck(Math.Abs(previousNow.Top - before.Top) < .01 && Math.Abs(nextNow.Top - after.Top - (grown.Height - empty.Height)) < .01 && CardByTitle("Other column") == otherBefore, "Growing blank moves only following cards in " + group + scenarioSuffix + " zoom" + N(zoom));
        string name = "grow-" + (group == "Plans" ? "task" : "watch") + scenarioSuffix + "-zoom" + N(zoom);
        var active = Capture(input, false, name + "-edit"); string value = input.Text;
        AuditCheck((bool)Call("FinishInline", false), "Expanded text saves in " + group);
        Layout();
        var display = Capture(Display("Edit " + value, value), false, name + "-display"); Compare(active, display, name, "save-grown", false); cases++;
        window.Edit(EntryByTitle(value), group); Layout(); input = (TextBox)DraftField("Input"); input.Text = ""; Layout();
        var cleared = Capture(input, false, name + "-clear-edit");
        AuditCheck((bool)Call("FinishInline", false), "Cleared row remains saved in " + group);
        Layout();
        var emptyDisplay = Display("Edit empty item 2 in " + group, ""); Compare(cleared, Capture(emptyDisplay, false, name + "-clear-display"), name, "save-empty", true); cases++;
        AuditCheck(CardByTitle("Before") == before && CardByTitle("After") == after && CardByTitle("Other column") == otherBefore, "Clearing restores original row and sibling geometry in " + group);
    }
    static void AuditScroll(string group, double zoom) {
        AuditConfigure(900, zoom, 240);
        var entries = new List<Entry>(); foreach (string column in new[] { NoteStore.ReadingGroup, "Plans" }) for (int i = 0; i < 18; i++) entries.Add(new Entry { Group = column, Title = (column == "Plans" ? "Task " : "Read ") + i });
        Fixture(entries.ToArray());
        foreach (var pair in Scrolls()) pair.Value.ScrollToVerticalOffset(pair.Key == group ? 185 : 90); Layout();
        var scrolls = Scrolls(); var offsets = scrolls.ToDictionary(x => x.Key, x => x.Value.VerticalOffset);
        string prefix = group == "Plans" ? "Task " : "Read ";
        int targetIndex = group == "Plans" ? 6 : 3; string target = prefix + targetIndex, neighbor = prefix + (targetIndex + 1);
        Rect before = CardByTitle(target), sibling = CardByTitle(neighbor);
        window.Edit(EntryByTitle(target), group); Layout();
        AuditCheck(Bounds(Host((TextBox)DraftField("Input"), false)) == before && CardByTitle(neighbor) == sibling && Scrolls().All(x => Math.Abs(x.Value.VerticalOffset - offsets[x.Key]) < .01), "Entering middle scrolled row preserves both column offsets and siblings in " + group + " zoom" + N(zoom));
        var active = Scrolls()[group]; active.ScrollToVerticalOffset(active.VerticalOffset + 43); Layout(); double moved = active.VerticalOffset - offsets[group];
        AuditCheck(Math.Abs(Bounds(Host((TextBox)DraftField("Input"), false)).Top - before.Top + moved * zoom) < .01 && Math.Abs(CardByTitle(neighbor).Top - sibling.Top + moved * zoom) < .01, "Editor and sibling move together during scroll in " + group + " zoom" + N(zoom));
        Rect scrolled = Bounds(Host((TextBox)DraftField("Input"), false)); double expectedOffset = active.VerticalOffset;
        bool saved = (bool)Call("FinishInline", false); Layout();
        AuditCheck(saved && CardByTitle(target) == scrolled && Math.Abs(Scrolls()[group].VerticalOffset - expectedOffset) < .01, "Leaving scrolled row preserves its position in " + group + " zoom" + N(zoom));
    }
    static void ExtendedAudit(List<string> kinds) {
        foreach (double width in new[] { 340.0, 600.0, 1280.0 }) foreach (double zoom in new[] { .8, 1.5 }) foreach (double watching in new[] { 170.0, 440.0 }) {
            AuditLayout(width, zoom, watching);
            if (width == 340) continue;
            foreach (string kind in kinds) {
                Run(kind, Samples(kind).First(x => x.Name == "wrapped-cjk"), 2, zoom);
                Run(kind, Samples(kind).First(x => x.Name == "empty"), 2, zoom);
            }
        }
        ExtendedTransitions();
        File.WriteAllLines(Path.Combine(output, "layout-metrics.csv"), layoutReport);
        Console.WriteLine(auditChecks + " additional layout/transition/scroll checks.");
    }
    static void ExtendedTransitions() {
        foreach (double zoom in new[] { .8, 1.5 }) foreach (string group in new[] { "Plans", NoteStore.ReadingGroup }) {
            AuditTransition(group, 900, zoom, 240); AuditScroll(group, zoom);
        }
    }
}
