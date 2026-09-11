using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shike;

// Renders real WPF controls offscreen. No HWND is shown and no desktop pixels
// are read. Assertions compare displayed colors across editing states.
static class ColumnColorTests {
    sealed class Paint {
        public double R, G, B, A;
        public override string ToString() { return string.Format("rgb({0:0},{1:0},{2:0}), alpha {3:0}", R, G, B, A); }
    }
    static int checks, failures;
    static string output;
    static double layoutWidth = 1000;
    static MainWindow window;
    static FrameworkElement root;
    static void Check(bool condition, string label) {
        checks++; if (!condition) failures++;
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject value) {
        yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(value, i))) yield return child;
    }
    static T Named<T>(string name) where T : DependencyObject {
        var found = Descendants(root).OfType<T>().FirstOrDefault(x => AutomationProperties.GetName(x) == name);
        if (found == null) throw new InvalidOperationException("Missing rendered control: " + name);
        var input = found as TextBox;
        if (input != null && input.IsReadOnly) throw new InvalidOperationException("Expected an active editor, but found a display field: " + name);
        return found;
    }
    static IEnumerable<string> VisibleText(DependencyObject parent) {
        return Descendants(parent).OfType<TextBlock>().Where(x => x.Visibility == Visibility.Visible && x.ActualWidth > 0 && x.ActualHeight > 0).Select(x => x.Text)
            .Concat(Descendants(parent).OfType<TextBox>().Where(x => x.Visibility == Visibility.Visible && x.ActualWidth > 0 && x.ActualHeight > 0).Select(x => x.Text));
    }
    static FrameworkElement EmptyCard(string group) {
        // Empty columns have no fabricated card; sample a blank explicitly
        // created through the same header action used by a person.
        Named<Button>("Add to " + group).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(); Finish();
        return PillFor(Named<Button>("Edit empty item 1 in " + group));
    }
    static void NoPlaceholders(string state) {
        string[] forbidden = { "Name", "Paste a link", "Add item", "Topic name" };
        var visible = VisibleText(root).Where(x => forbidden.Contains(x.Trim())).Distinct().ToArray();
        Check(visible.Length == 0, state + " has no visible placeholder labels" + (visible.Length == 0 ? "" : ": " + string.Join(", ", visible)));
    }
    static bool Transparent(Brush brush) {
        if (brush == null || brush.Opacity == 0) return true;
        var solid = brush as SolidColorBrush;
        return solid != null && solid.Color.A == 0;
    }
    static void NoTextButtonFlash(Button button, string state) {
        bool paintsOnInteraction = false;
        foreach (var trigger in button.Template.Triggers.OfType<Trigger>()) {
            if (trigger.Property != UIElement.IsMouseOverProperty && trigger.Property != Button.IsPressedProperty && trigger.Property != UIElement.IsKeyboardFocusedProperty) continue;
            if (trigger.Setters.OfType<Setter>().Any(x => x.Property == Panel.BackgroundProperty || x.Property == Border.BackgroundProperty || x.Property == Control.BackgroundProperty)) paintsOnInteraction = true;
        }
        Check(Transparent(button.Background) && button.BorderThickness == new Thickness(0) && button.FocusVisualStyle == null && !paintsOnInteraction, state + " has no rectangular hover, press or focus background");
    }
    static void NoCardOutline(FrameworkElement card, string state) {
        var border = card as Border ?? Descendants(card).OfType<Border>().FirstOrDefault(x => x.ActualHeight >= 40 && x.Background is LinearGradientBrush);
        Check(border != null && border.BorderThickness == new Thickness(0), state + " has no outer colored-card outline");
        var button = card as Button;
        if (button != null) {
            bool paintsBorder = button.Template.Triggers.OfType<Trigger>().SelectMany(x => x.Setters.OfType<Setter>()).Any(x => x.Property == Border.BorderBrushProperty || x.Property == Border.BorderThicknessProperty || x.Property == Control.BorderBrushProperty || x.Property == Control.BorderThicknessProperty);
            Check(button.BorderThickness == new Thickness(0) && !paintsBorder, state + " does not add an outer outline on hover or focus");
        }
    }
    static void Layout() {
        root.Measure(new Size(layoutWidth, 600)); root.Arrange(new Rect(0, 0, layoutWidth, 600)); root.UpdateLayout();
    }
    static void WaitForCompletionAppearance() {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); Layout();
    }
    static void Finish(bool advance = false) {
        var method = typeof(MainWindow).GetMethod("FinishInline", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("This build has no inline editor to test.");
        if (!(bool)method.Invoke(window, new object[] { advance })) throw new InvalidOperationException("Could not finish the isolated inline edit.");
        Layout();
    }
    static FrameworkElement PillFor(DependencyObject control) {
        for (DependencyObject parent = VisualTreeHelper.GetParent(control); parent != null; parent = VisualTreeHelper.GetParent(parent)) {
            var border = parent as Border;
            if (border != null && border.Background != null && border.ActualHeight >= 40) return border;
        }
        throw new InvalidOperationException("Cannot find the card containing the test control.");
    }
    static Paint Sample(FrameworkElement card, string name, bool bottom = false) {
        Layout();
        double width = card.ActualWidth, height = card.ActualHeight;
        if (width < 80 || height < 40) throw new InvalidOperationException("The test card was not laid out.");
        const double scale = 2;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) {
            var source = new VisualBrush(card) { Stretch = Stretch.Fill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            context.DrawRectangle(source, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
        // The upper interior strip is clear of text, controls, rounded corners,
        // and the outline in all three card states. Unpremultiply to compare
        // material hue independently of slight row-height/opacity differences.
        int centerX = (int)(width * scale * .70), centerY = (int)((bottom ? height - 6 : 6) * scale), count = 0;
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        var result = new Paint();
        for (int y = centerY - 1; y <= centerY + 1; y++) for (int x = centerX - 2; x <= centerX + 2; x++) {
            int offset = y * stride + x * 4; double alpha = pixels[offset + 3];
            if (alpha <= 0) throw new InvalidOperationException("The rendered card sample is transparent.");
            result.B += pixels[offset] * 255.0 / alpha;
            result.G += pixels[offset + 1] * 255.0 / alpha;
            result.R += pixels[offset + 2] * 255.0 / alpha;
            result.A += alpha; count++;
        }
        result.R /= count; result.G /= count; result.B /= count; result.A /= count;
        return result;
    }
    static void Same(Paint expected, Paint actual, string label) {
        double difference = Math.Max(Math.Abs(expected.R - actual.R), Math.Max(Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B)));
        Check(difference <= 3, label + ": " + expected + " -> " + actual);
    }
    static double Lightness(Paint color) { return (color.R + color.G + color.B) / 3; }
    static double[] Hue(Paint color) {
        double lowest = Math.Min(color.R, Math.Min(color.G, color.B));
        double range = Math.Max(color.R, Math.Max(color.G, color.B)) - lowest;
        return new[] { (color.R - lowest) / Math.Max(1, range), (color.G - lowest) / Math.Max(1, range), (color.B - lowest) / Math.Max(1, range) };
    }
    static double HueDegrees(Paint color) {
        double highest = Math.Max(color.R, Math.Max(color.G, color.B)), lowest = Math.Min(color.R, Math.Min(color.G, color.B));
        double range = highest - lowest;
        if (range < 1) throw new InvalidOperationException("The rainbow test card has no discernible hue.");
        double hue = highest == color.R ? (color.G - color.B) / range : highest == color.G ? 2 + (color.B - color.R) / range : 4 + (color.R - color.G) / range;
        hue *= 60; return hue < 0 ? hue + 360 : hue;
    }
    static void Rainbow(NoteStore store, bool unnamed = false) {
        string[] names = { NoteStore.ReadingGroup, "First topic", "Second topic", "Third topic", "Fourth topic", "Fifth topic", "Sixth topic" };
        string[] hues = { "burgundy", "orange", "yellow", "green", "cyan", "blue", "violet" };
        double[] minimum = { -20, 18, 45, 115, 170, 205, 245 }, maximum = { 18, 48, 68, 165, 205, 240, 280 };
        File.WriteAllText(store.CurrentPath, unnamed ? "# Test\n\n## " + NoteStore.ReadingGroup + "\n" : "# Test\n\n" + string.Join("\n\n", names.Select(x => "## " + x)) + "\n");
        window.Refresh(); layoutWidth = 1800; Layout();
        if (unnamed) {
            for (int i = 0; i < 6; i++) {
                Named<Button>("New topic").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
                Check(store.Load().Groups.Count == i + 2, "Unnamed rainbow topic " + (i + 1) + " persists immediately when added");
                Finish();
            }
            names = new[] { NoteStore.ReadingGroup }.Concat(store.Load().Groups.Where(x => x != NoteStore.ReadingGroup)).ToArray();
            Check(names.Skip(1).All(NoteDocument.IsEmptyGroup) && names.Skip(1).Select(NoteDocument.GroupTitle).All(x => x == ""), "All six unnamed rainbow columns preserve empty display names");
            Check(!Descendants(root).OfType<TextBlock>().Select(x => x.Text).Concat(Descendants(root).OfType<TextBox>().Select(x => x.Text)).Any(x => x.Contains("<!-- shike-topic:")), "Internal unnamed-topic identities are absent from rendered text");
        }
        double previousHue = -100, previousX = -1, firstY = 0;
        bool horizontal = true;
        for (int i = 0; i < names.Length; i++) {
            string label = NoteDocument.IsEmptyGroup(names[i]) ? "empty topic " + i : names[i];
            var card = EmptyCard(label);
            Paint color = Sample(card, (unnamed ? "unnamed-" : "") + "rainbow-column-" + i);
            Check(Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) > 100, "Rainbow column " + (i + 1) + " retains a saturated, distinct hue");
            double hue = HueDegrees(color); if (i == 0 && hue > 330) hue -= 360;
            Check(hue >= minimum[i] && hue <= maximum[i], "Rainbow column " + (i + 1) + " (" + label + ") is " + hues[i] + ": hue " + hue.ToString("F1") + ", " + color);
            if (i > 0) Check(hue > previousHue, "Rainbow hue advances from " + hues[i - 1] + " to " + hues[i] + " from left to right");
            Point position = card.TransformToAncestor(root).Transform(new Point());
            if (i == 0) firstY = position.Y;
            horizontal &= position.X > previousX && Math.Abs(position.Y - firstY) < 1;
            previousHue = hue; previousX = position.X;
        }
        Check(horizontal, "All seven rainbow columns, including the fixed Watching list, are arranged in order on the wide offscreen canvas");
        Check(Named<ScrollViewer>("Topics").ScrollableWidth < 2, "Seven fitted columns do not show unnecessary horizontal overflow (overflow " + Named<ScrollViewer>("Topics").ScrollableWidth + ", viewport " + Named<ScrollViewer>("Topics").ViewportWidth + ")");
        NoPlaceholders(unnamed ? "Unnamed rainbow columns" : "Empty rainbow columns");
    }
    static Point Position(Visual visual) { return visual.TransformToAncestor(root).Transform(new Point()); }
    static void ColumnLayout(NoteStore store) {
        string[] names = { NoteStore.ReadingGroup, "First topic", "Second topic" };
        string text = "# Test\n\n";
        foreach (string name in names) {
            text += "## " + name + "\n";
            for (int i = 0; i < 20; i++) text += "- [ ] " + name + " row " + (i + 1) + "\n";
            text += "\n";
        }
        File.WriteAllText(store.CurrentPath, text); window.Refresh(); layoutWidth = 1000; Layout(); Layout();
        var watching = Named<Border>("Column " + NoteStore.ReadingGroup);
        var firstTopic = Named<Border>("Column First topic");
        var secondTopic = Named<Border>("Column Second topic");
        Check(Position(watching).X < Position(firstTopic).X && Position(firstTopic).X < Position(secondTopic).X, "Watching list stays first, before the horizontal topics");
        Check(watching.ActualWidth > firstTopic.ActualWidth * 1.15 && watching.ActualWidth < firstTopic.ActualWidth * 1.35, "Watching list is approximately one quarter wider than a topic");
        var divider = Named<Border>("Watching list divider");
        Check(divider.Visibility == Visibility.Visible && divider.ActualWidth >= 1 && Position(divider).X > Position(watching).X + watching.ActualWidth - 2 && Position(divider).X < Position(firstTopic).X, "A subtle divider separates Watching list from the topics");
        var watchScroll = Named<ScrollViewer>("Items in " + NoteStore.ReadingGroup);
        var firstScroll = Named<ScrollViewer>("Items in First topic");
        var secondScroll = Named<ScrollViewer>("Items in Second topic");
        Check(watchScroll.ScrollableHeight > 500 && firstScroll.ScrollableHeight > 300 && secondScroll.ScrollableHeight > 300, "Long columns each have their own constrained vertical scrolling range");
        // Watching still uses a static label in the intermediate alignment
        // build; its editable heading uses the same read-only display field.
        FrameworkElement watchHeading = Descendants(watching).OfType<TextBox>().FirstOrDefault(x => x.IsReadOnly && x.Text == NoteStore.ReadingGroup);
        if (watchHeading == null) watchHeading = Descendants(watching).OfType<TextBlock>().First(x => x.Text == NoteStore.ReadingGroup);
        var firstHeading = Descendants(firstTopic).OfType<TextBox>().First(x => x.IsReadOnly && x.Text == "First topic");
        Point watchHeadingBefore = Position(watchHeading), firstHeadingBefore = Position(firstHeading);
        watchScroll.ScrollToVerticalOffset(145); Layout(); Layout();
        Check(watchScroll.VerticalOffset > 140 && firstScroll.VerticalOffset == 0 && secondScroll.VerticalOffset == 0, "Scrolling Watching list leaves both topic columns stationary");
        firstScroll.ScrollToVerticalOffset(82); Layout(); Layout();
        Check(Math.Abs(watchScroll.VerticalOffset - 145) < 1 && Math.Abs(firstScroll.VerticalOffset - 82) < 1 && secondScroll.VerticalOffset == 0, "Scrolling one topic preserves the other columns' positions");
        Check(Math.Abs(Position(watchHeading).Y - watchHeadingBefore.Y) < 1 && Math.Abs(Position(firstHeading).Y - firstHeadingBefore.Y) < 1, "Column headings stay fixed while their lists scroll");
        window.Refresh(); Layout(); Layout();
        Check(Math.Abs(Named<ScrollViewer>("Items in " + NoteStore.ReadingGroup).VerticalOffset - 145) < 1 && Math.Abs(Named<ScrollViewer>("Items in First topic").VerticalOffset - 82) < 1, "Independent vertical positions survive a list rebuild");
        layoutWidth = 640; Layout(); Layout();
        var topics = Named<ScrollViewer>("Topics"); watching = Named<Border>("Column " + NoteStore.ReadingGroup);
        double fixedX = Position(watching).X;
        Check(topics.ScrollableWidth > 50 && Math.Abs(Position(Named<Border>("Column First topic")).Y - Position(Named<Border>("Column Second topic")).Y) < 1, "Narrow windows overflow topics horizontally without wrapping under Watching list");
        topics.ScrollToHorizontalOffset(75); Layout(); Layout();
        Check(topics.HorizontalOffset > 50 && Math.Abs(Position(watching).X - fixedX) < 1, "Watching list remains fixed while the topics scroll horizontally");
        layoutWidth = 1000;
    }
    static void WatchingFields(NoteStore store) {
        File.WriteAllText(store.CurrentPath, "# Test\n\n## " + NoteStore.ReadingGroup + "\n\n## First topic\n");
        window.Refresh(); layoutWidth = 1000; Layout();
        window.Edit(null, NoteStore.ReadingGroup); Layout();
        Check(Descendants(root).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "Item link"), "Watching cards expose title and link together during inline editing");
        var titleInput = Named<TextBox>("Item text"); var linkInput = Named<TextBox>("Item link");
        Check(Position(titleInput).Y < Position(linkInput).Y, "The Watching title is above its link field");
        Check(Transparent(titleInput.Background) && Transparent(linkInput.Background) && titleInput.BorderThickness == new Thickness(0) && linkInput.BorderThickness == new Thickness(0), "Watching inline fields retain their card material without rectangular editor surfaces");
        NoPlaceholders("Blank Watching editor");
        Paint blankColor = Sample(PillFor(titleInput), "watching-blank-two-fields");
        NoCardOutline(PillFor(titleInput), "Watching blank editor");
        Finish();
        var emptyLink = Named<Button>("Edit link empty item 1 in " + NoteStore.ReadingGroup);
        Check(emptyLink.ActualHeight >= 18 && emptyLink.ActualWidth > 80, "Saved empty Watching cards retain a usable blank link target");
        NoPlaceholders("Saved empty Watching card");
        NoCardOutline(PillFor(emptyLink), "Watching saved blank card");
        NoTextButtonFlash(emptyLink, "Empty Watching link target");
        emptyLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Check(Descendants(root).OfType<TextBox>().Any(x => AutomationProperties.GetName(x) == "Item link"), "The blank link target opens inline editing in the same card");
        string url = "https://example.com/watch/" + new string('v', 180);
        Named<TextBox>("Item link").Text = url; Finish();
        var linkOnly = store.Load().Entries.Single();
        Check(linkOnly.Title == "" && linkOnly.Link == url, "A Watching URL can be saved before its title without invented content");
        var titleButton = Named<Button>("Edit " + url);
        var linkButton = Named<Button>("Edit link " + url);
        NoTextButtonFlash(titleButton, "Saved Watching name target");
        NoTextButtonFlash(linkButton, "Saved Watching URL target");
        var pill = PillFor(titleButton);
        NoCardOutline(pill, "Watching saved URL card");
        Check(Position(titleButton).Y < Position(linkButton).Y, "A saved Watching card keeps the title above the saved URL");
        var savedLinkText = Descendants(linkButton).OfType<TextBox>().First(x => x.IsReadOnly && x.Text == url);
        Check(!savedLinkText.Focusable && !savedLinkText.IsHitTestVisible && !savedLinkText.IsTabStop, "Saved Watching text delegates focus and actions to its enclosing button");
        Check(savedLinkText.TextWrapping == TextWrapping.Wrap && savedLinkText.ActualWidth < pill.ActualWidth && savedLinkText.ActualHeight > 25, "Long unbroken Watching URLs wrap within the card instead of overflowing");
        Check(Descendants(pill).OfType<Border>().Any(x => Math.Abs(x.ActualHeight - 1) < .1 && x.Background != null && x.Margin.Top == 5 && x.Margin.Bottom == 4), "A soft divider separates title and link inside Watching cards");
        Same(blankColor, Sample(pill, "watching-link-only"), "Filling only the link retains the Watching card's first-position hue");
        Check(Descendants(root).OfType<Button>().Any(x => AutomationProperties.GetName(x) == "Open " + url), "The separate arrow retains an explicit Open action for a saved URL");
        double savedHeight = pill.ActualHeight;
        linkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        var editingPill = PillFor(Named<TextBox>("Item link"));
        Check(Math.Abs(editingPill.ActualHeight - savedHeight) < 4, "Editing a long Watching URL keeps the card height stable (saved " + savedHeight + ", editing " + editingPill.ActualHeight + ", saved link width " + savedLinkText.ActualWidth + ", input width " + Named<TextBox>("Item link").ActualWidth + ")");
        Check(Named<TextBox>("Item link").Text == url, "Clicking a saved URL edits the existing URL in place");
        Named<TextBox>("Item text").Text = "Watch later"; Finish();
        Same(blankColor, Saved("Watch later", "watching-filled-two-fields"), "Filling title and link preserves the Watching column's hue");
        string longTitle = string.Join(" ", Enumerable.Repeat("A detailed viewing title", 7));
        string note = "A preserved note with enough detail to wrap beneath the viewing link.";
        var previous = store.Load().Entries.Single();
        store.Mutate(previous, new Entry { Group = NoteStore.ReadingGroup, Title = longTitle, Link = url, Note = note }, NoteStore.ReadingGroup);
        window.Refresh(); Layout();
        var savedLong = PillFor(Named<Button>("Edit " + longTitle));
        double fullHeight = savedLong.ActualHeight;
        Check(Descendants(savedLong).OfType<TextBlock>().Any(x => x.Text == note), "Watching metadata remains visible below its title and link");
        Named<Button>("Edit " + longTitle).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        var editingLong = PillFor(Named<TextBox>("Item text"));
        Check(Math.Abs(editingLong.ActualHeight - fullHeight) < 4, "Wrapped Watching title, URL and note keep their layout while editing (saved " + fullHeight + ", editing " + editingLong.ActualHeight + ")");
        Check(Descendants(editingLong).OfType<TextBlock>().Any(x => x.Text == note), "Editing preserves the visible Watching note");
        Finish();
    }
    static void Lighter(Paint earlier, Paint later, string label) {
        Check(Lightness(later) > Lightness(earlier) + 3, label + " is visibly lighter: " + earlier + " -> " + later);
        double[] start = Hue(earlier), end = Hue(later);
        Check(start.Zip(end, (a, b) => Math.Abs(a - b)).Max() < .04, label + " stays in the same hue family");
    }
    static void VerticalGradient(FrameworkElement card, string name) {
        Lighter(Sample(card, name + "-top"), Sample(card, name + "-bottom", true), name + " top-to-bottom gradient");
    }
    static Paint Saved(string title, string name) { return Sample(PillFor(Named<Button>("Edit " + title)), name); }
    static Paint SavedBlank(string group, int position, string name) {
        return Sample(PillFor(Named<Button>("Edit empty item " + (position + 1) + " in " + group)), name);
    }
    static void PersistentBlankGradients(NoteStore store) {
        File.WriteAllText(store.CurrentPath, "# Test\n\n## " + NoteStore.ReadingGroup + "\n\n## First topic\n\n## Second topic\n");
        window.Refresh(); layoutWidth = 1000; Layout();
        window.Edit(null, "Second topic"); Layout();
        var colors = new Paint[3];
        for (int i = 0; i < colors.Length; i++) {
            colors[i] = Sample(PillFor(Named<TextBox>("Item text")), "consecutive-blank-editor-" + i);
            VerticalGradient(PillFor(Named<TextBox>("Item text")), "consecutive-blank-gradient-" + i);
            if (i > 0) Lighter(colors[i - 1], colors[i], "Successive persisted blank row " + (i + 1));
            if (i < colors.Length - 1) Finish(true);
        }
        Finish();
        Check(store.Load().Entries.Count == 3 && store.Load().Entries.All(x => x.Title == ""), "Repeated Enter leaves three persisted blank rows with no assumed text");
        NoPlaceholders("Consecutive saved blank rows");
        for (int i = 0; i < colors.Length; i++) Same(colors[i], SavedBlank("Second topic", i, "consecutive-blank-saved-" + i), "Blank row " + (i + 1) + " preserves its gradient position after editing ends");
        var third = store.Load().Entries[2]; window.Edit(third, "Second topic"); Layout();
        Same(colors[2], Sample(PillFor(Named<TextBox>("Item text")), "reopened-third-blank"), "Reopening a stored blank preserves its gradient position");
        Named<TextBox>("Item text").Text = "Filled third row"; Finish();
        Same(colors[2], Saved("Filled third row", "filled-third-blank"), "Filling a stored blank preserves its gradient position");
        Named<Button>("Edit Filled third row").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Named<TextBox>("Item text").Text = ""; Finish();
        Same(colors[2], SavedBlank("Second topic", 2, "cleared-third-blank"), "Clearing a filled row keeps the blank at the same gradient position");
        Check(store.Load().Entries.Count == 3, "Clearing text keeps all three persisted rows");
        Named<CheckBox>("Remove empty item 1 in Second topic").IsChecked = true; Layout();
        Check(store.Load().Entries.Count == 2, "Removing the first blank removes only that row");
        Same(colors[0], SavedBlank("Second topic", 0, "blank-after-delete-first"), "The next blank adopts the deeper first position after removal");
        Same(colors[1], SavedBlank("Second topic", 1, "blank-after-delete-second"), "The last blank adopts the second gradient position after removal");
    }
    [STAThread] static int Main(string[] args) {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            string state = Path.Combine(Path.GetTempPath(), "shike-column-colors-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(state);
            var store = new NoteStore(state, Path.Combine(state, "backups"));
            File.WriteAllText(store.CurrentPath, "# Test\n\n## " + NoteStore.ReadingGroup + "\n\n## First topic\n\n## Second topic\n");
            Theme.SetDark(true);
            window = new MainWindow(store, new Settings { Width = 1000, Height = 600, Transparency = 45, Pinned = false }, state);
            root = (FrameworkElement)window.Content; window.Content = null;
            // Detaching for an offscreen layout must preserve the real
            // window's inherited typography rather than the Windows locale.
            root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, window.FontFamily);
            root.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, window.FontSize);
            TextOptions.SetTextFormattingMode(root, TextOptions.GetTextFormattingMode(window));
            root.Resources = Theme.Resources(); root.UseLayoutRounding = true; root.SnapsToDevicePixels = true;
            VisualTreeHelper.SetRootDpi(root, new DpiScale(1, 1)); Layout();
            string[] groups = { NoteStore.ReadingGroup, "First topic", "Second topic" };
            string[] titles = { "Watching saved", "First saved", "Second saved" };
            var colors = new Dictionary<string, Paint>();
            for (int i = 0; i < groups.Length; i++) {
                string group = groups[i];
                var button = EmptyCard(group);
                colors[group] = Sample(button, "empty-" + i);
                NoCardOutline(button, group + " empty card");
                VerticalGradient(button, "empty-column-" + i);
            }
            var burgundy = colors[groups[0]];
            double burgundySaturation = (burgundy.R - Math.Min(burgundy.G, burgundy.B)) / burgundy.R;
            Check(burgundy.R >= 120 && burgundy.R <= 190 && burgundy.G <= 35 && burgundy.B >= burgundy.G + 15 && burgundy.R > burgundy.B + 65 && burgundySaturation >= .7, "Watching list starts burgundy with a dark red base and blue undertone: " + burgundy);
            NoPlaceholders("Initial empty columns");
            Check(colors[groups[1]].R > colors[groups[1]].G + 40 && colors[groups[1]].G > colors[groups[1]].B + 35, "The first topic after Watching list starts orange");
            Check(colors[groups[2]].G > colors[groups[2]].R * .85 && colors[groups[2]].B < colors[groups[2]].G * .65, "The second topic in the third position starts yellow");
            // Test all empty editors before saving anything. This catches the
            // second-column draft mistakenly borrowing the first row's color.
            for (int i = 0; i < groups.Length; i++) {
                window.Edit(store.Load().Entries.Single(x => x.Group == groups[i]), groups[i]); Layout();
                NoPlaceholders(groups[i] + " blank editor");
                Same(colors[groups[i]], Sample(PillFor(Named<TextBox>("Item text")), "empty-editor-" + i), groups[i] + " retains its empty color when editing begins");
                NoCardOutline(PillFor(Named<TextBox>("Item text")), groups[i] + " inline blank card");
                VerticalGradient(PillFor(Named<TextBox>("Item text")), "empty-editor-gradient-" + i);
                Finish();
                Check(store.Load().Entries.Count(x => x.Group == groups[i] && x.Title == "") == 1, groups[i] + " keeps the newly created blank row after editing ends");
                Same(colors[groups[i]], SavedBlank(groups[i], 0, "first-blank-saved-" + i), groups[i] + " saved blank retains its first-position color");
                NoCardOutline(PillFor(Named<Button>("Edit empty item 1 in " + groups[i])), groups[i] + " persisted blank card");
            }
            for (int i = 0; i < groups.Length; i++) {
                window.Edit(store.Load().Entries.Single(x => x.Group == groups[i]), groups[i]); Layout();
                Same(colors[groups[i]], Sample(PillFor(Named<TextBox>("Item text")), "reopened-first-blank-" + i), groups[i] + " stored blank keeps its first-position color when reopened");
                Named<TextBox>("Item text").Text = titles[i]; Finish();
                Same(colors[groups[i]], Saved(titles[i], "saved-" + i), groups[i] + " retains its color after saving");
                NoCardOutline(PillFor(Named<Button>("Edit " + titles[i])), groups[i] + " filled card");
                VerticalGradient(PillFor(Named<Button>("Edit " + titles[i])), "saved-gradient-" + i);
                Named<Button>("Edit " + titles[i]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
                Same(colors[groups[i]], Sample(PillFor(Named<TextBox>("Item text")), "existing-editor-" + i), groups[i] + " retains its color when an existing row is edited");
                Finish();
            }
            window.Edit(null, groups[0]); Layout();
            Paint extraEditor = Sample(PillFor(Named<TextBox>("Item text")), "additional-editor");
            Lighter(colors[groups[0]], extraEditor, "The lower editor in the first topic");
            Named<TextBox>("Item text").Text = "Additional first item"; Finish();
            Same(extraEditor, Saved("Additional first item", "additional-saved"), "Saving the lower first-topic row preserves its gradient position");
            VerticalGradient(PillFor(Named<Button>("Edit Additional first item")), "additional-saved-gradient");
            for (int i = 1; i < groups.Length; i++) Same(colors[groups[i]], Saved(titles[i], "after-add-" + i), groups[i] + " is unaffected by an item added in another column");
            string[] lowerTitles = { "Additional first item", "Additional second item", "Additional reading item" };
            var lowerColors = new Dictionary<string, Paint>(); lowerColors[groups[0]] = extraEditor;
            for (int i = 1; i < groups.Length; i++) {
                window.Edit(null, groups[i]); Layout();
                lowerColors[groups[i]] = Sample(PillFor(Named<TextBox>("Item text")), "lower-editor-" + i);
                Lighter(colors[groups[i]], lowerColors[groups[i]], groups[i] + " lower-row editor");
                Named<TextBox>("Item text").Text = lowerTitles[i]; Finish();
                Same(lowerColors[groups[i]], Saved(lowerTitles[i], "lower-saved-" + i), groups[i] + " lower row retains its gradient position after saving");
                VerticalGradient(PillFor(Named<Button>("Edit " + lowerTitles[i])), "lower-saved-gradient-" + i);
                Named<Button>("Edit " + lowerTitles[i]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
                Same(lowerColors[groups[i]], Sample(PillFor(Named<TextBox>("Item text")), "lower-existing-editor-" + i), groups[i] + " lower row retains its gradient position when edited again");
                Finish();
            }
            for (int i = 0; i < groups.Length; i++) {
                Same(colors[groups[i]], Saved(titles[i], "all-added-upper-" + i), groups[i] + " first-row color is independent of other columns' row counts");
                Same(lowerColors[groups[i]], Saved(lowerTitles[i], "all-added-lower-" + i), groups[i] + " lower-row color is independent of other columns' row counts");
            }
            Named<CheckBox>("Complete " + titles[0]).IsChecked = true; WaitForCompletionAppearance();
            Check(!Descendants(root).OfType<CheckBox>().Any(x => AutomationProperties.GetName(x) == "Complete " + titles[0] || AutomationProperties.GetName(x) == "Restore " + titles[0]), "The completed row disappears after its brief dot feedback during the color regression scenario");
            Same(colors[groups[0]], Saved("Additional first item", "after-complete-0"), "The remaining first-topic row adopts the first position's deeper color after completion");
            for (int i = 1; i < groups.Length; i++) {
                Same(colors[groups[i]], Saved(titles[i], "after-complete-" + i), groups[i] + " first row is unaffected by completion in another column");
                Same(lowerColors[groups[i]], Saved(lowerTitles[i], "lower-after-complete-" + i), groups[i] + " lower row is unaffected by completion in another column");
            }
            PersistentBlankGradients(store);
            ColumnLayout(store);
            WatchingFields(store);
            Rainbow(store);
            Rainbow(store, true);
            Console.WriteLine("Column color checks: " + checks + ", failures: " + failures + ". Offscreen artifacts: " + output);
            return failures == 0 ? 0 : 1;
        } catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        finally { if (window != null) window.Exit(); app.Shutdown(); }
    }
}
