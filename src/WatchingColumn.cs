using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Shike {
    public sealed partial class MainWindow {
        Thumb watchingResize;
        bool watchingResizeActive;
        double watchingDragOriginal, watchingDragWidth;
        string undoWatchingBefore, undoWatchingAfter;

        string WatchingTitle { get { return config.WatchingTitle ?? NoteStore.ReadingGroup; } }

        void RememberUndo(string before, string after, string watchingBefore = null, string watchingAfter = null) {
            undoBefore = before; undoAfter = after;
            undoWatchingBefore = watchingBefore; undoWatchingAfter = watchingAfter;
        }

        void SaveWatchingTitle(string title) {
            string previous = config.WatchingTitle;
            config.WatchingTitle = title;
            try { config.Save(stateDirectory); }
            catch { config.WatchingTitle = previous; throw; }
        }

        bool CommitWatchingTitle(InlineDraft draft, out Entry saved, out string group, bool advance) {
            saved = null; group = NoteStore.ReadingGroup;
            try {
                string title = draft.Text.Trim();
                if (title.Length > 60 || title.Contains("\n") || title.Contains("\r")) throw new ArgumentException("Enter a short title.");
                var latest = Store.Load();
                string updated = latest.Text, previousTitle = WatchingTitle;
                Entry following = null;
                if (advance) updated = latest.InsertAfter(null, new Entry { Group = group, Title = "", Link = "", Note = "" }, group, out following);
                bool changedTitle = title != previousTitle;
                if (changedTitle) SaveWatchingTitle(title);
                try { if (updated != latest.Text) Store.Save(latest.Text, updated); }
                catch { if (changedTitle) SaveWatchingTitle(previousTitle); throw; }
                if (changedTitle || updated != latest.Text) RememberUndo(latest.Text, updated, changedTitle ? previousTitle : null, changedTitle ? title : null);
                document = NoteDocument.Parse(updated);
                draft.Following = following == null ? null : document.Entries.Find(x => x.Line == following.Line && x.Group == NoteStore.ReadingGroup);
                inline = null;
                return true;
            } catch (Exception ex) { ShowInlineError(draft, ex.Message); return false; }
        }

        bool UndoWatchingTitle() {
            if (undoWatchingBefore == null) return false;
            try {
                if (WatchingTitle != undoWatchingAfter || Store.Load().Text != undoAfter) throw new InvalidOperationException("Your list changed elsewhere. Refresh before undoing.");
                string currentTitle = WatchingTitle;
                SaveWatchingTitle(undoWatchingBefore);
                try { if (undoBefore != undoAfter) Store.Save(undoAfter, undoBefore); }
                catch { SaveWatchingTitle(currentTitle); throw; }
                RememberUndo(null, null); Refresh(); Notify("Undone", false, false);
            } catch (Exception ex) { Notify(ex.Message, false, true); }
            return true;
        }

        void BuildWatchingDivider() {
            var host = new Grid(); Grid.SetColumn(host, 1); groups.Children.Add(host);
            columnDivider = new Border { Width = 1, Background = Theme.ColumnDivider(), Margin = new Thickness(0, 8, 0, 10), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(columnDivider, "Watching list divider"); host.Children.Add(columnDivider);
            watchingResize = new Thumb { Background = Brushes.Transparent, Cursor = Cursors.SizeWE, FocusVisualStyle = null };
            watchingResize.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate>");
            AutomationProperties.SetName(watchingResize, "Resize Watching list"); host.Children.Add(watchingResize);
            watchingResize.DragStarted += delegate(object sender, DragStartedEventArgs e) {
                if (!FinishInline(false)) { watchingResize.CancelDrag(); return; }
                watchingResizeActive = true; watchingDragOriginal = config.WatchingWidth;
                watchingDragWidth = groups.ColumnDefinitions[0].Width.Value;
            };
            watchingResize.DragDelta += delegate(object sender, DragDeltaEventArgs e) {
                if (!watchingResizeActive || Math.Abs(e.HorizontalChange) < 0.01) return;
                watchingDragWidth = ClampWatchingWidth(watchingDragWidth + e.HorizontalChange, AvailableColumnWidth);
                config.WatchingWidth = watchingDragWidth; UpdateColumns();
            };
            watchingResize.DragCompleted += delegate(object sender, DragCompletedEventArgs e) {
                if (!watchingResizeActive) return;
                watchingResizeActive = false;
                if (e.Canceled) { config.WatchingWidth = watchingDragOriginal; UpdateColumns(); }
                SaveSettings();
            };
            watchingResize.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if ((e.Key != Key.Left && e.Key != Key.Right) || Keyboard.Modifiers != ModifierKeys.None || !FinishInline(false)) return;
                config.WatchingWidth = ClampWatchingWidth(groups.ColumnDefinitions[0].Width.Value + (e.Key == Key.Left ? -16 : 16), AvailableColumnWidth);
                UpdateColumns(); SaveSettings(); e.Handled = true;
            };
        }

        static double ClampWatchingWidth(double width, double available) {
            // Reserve the creation area even before the first topic is added.
            double usable = Math.Max(1, available - 26);
            double minimumPane = Math.Min(100, usable / 2);
            double topicSpace = Math.Min(210, Math.Max(minimumPane, usable - 170));
            double maximum = usable - topicSpace;
            double minimum = Math.Min(170, maximum);
            return Math.Max(minimum, Math.Min(maximum, width));
        }

        double AvailableColumnWidth { get { return groups.ActualWidth > 0 ? groups.ActualWidth : 260; } }

        void WatchingColumnWidths(double available, int count, out double watchingWidth, out double topicWidth) {
            double gaps = Math.Max(0, count - 1) * 14;
            topicWidth = Math.Floor(Math.Max(210, Math.Min(320, (available - 26 - gaps) / (count + 1.25))));
            watchingWidth = count == 0 ? Math.Min(380, available) : Math.Floor(Math.Min(topicWidth * 1.25, Math.Max(170, available * 0.50)));
            watchingWidth = ClampWatchingWidth(watchingWidth, available);
            if (config.WatchingWidth > 0 && !double.IsInfinity(config.WatchingWidth)) {
                watchingWidth = ClampWatchingWidth(config.WatchingWidth, available);
                topicWidth = Math.Floor(Math.Max(210, Math.Min(320, (available - 26 - watchingWidth - gaps) / Math.Max(1, count))));
            }
        }
    }
}
