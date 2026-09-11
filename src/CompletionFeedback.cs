using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shike {
    public sealed partial class MainWindow {
        sealed class CompletionAppearance {
            public string Group, Raw;
            public int Line, ColorIndex;
            public long Until;
            public FrameworkElement Card;
            public bool Matches(Entry entry) { return entry.Done && entry.Group == Group && entry.Raw == Raw && entry.Line == Line; }
        }
        readonly List<CompletionAppearance> completionAppearances = new List<CompletionAppearance>();
        DispatcherTimer completionTimer;

        void ShowCompletionFeedback(Entry old, Entry next) {
            if (old == null || old.Done || next == null || !next.Done || old.Group != next.Group) return;
            // Mutation has already been durably saved. Resolve its actual line
            // in that transaction, so equal titles never share an appearance.
            var before = NoteDocument.Parse(undoBefore);
            var matches = before.Entries.Where(x => x.Group == old.Group && x.Raw == old.Raw).ToList();
            if (matches.Count != old.Duplicates || old.Occurrence >= matches.Count) return;
            int line = matches[old.Occurrence].Line;
            string raw = NoteDocument.Serialize(next, document.Newline);
            var saved = document.Entries.FirstOrDefault(x => x.Group == next.Group && x.Line == line && x.Raw == raw && x.Done);
            if (saved == null) return;
            completionAppearances.RemoveAll(x => x.Matches(saved));
            completionAppearances.Add(new CompletionAppearance { Group = saved.Group, Raw = saved.Raw, Line = saved.Line, Until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 180 / 1000 });
            if (completionTimer == null) {
                completionTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
                completionTimer.Tick += delegate { ExpireCompletionFeedback(); };
                Closed += delegate { completionTimer.Stop(); completionAppearances.Clear(); };
            }
            completionTimer.Start();
        }

        bool IsCompletionVisible(Entry entry) { return completionAppearances.Any(x => x.Matches(entry)); }

        void PrepareCompletionRender() {
            long now = Stopwatch.GetTimestamp();
            completionAppearances.RemoveAll(x => x.Until <= now || !document.Entries.Any(x.Matches));
            foreach (var appearance in completionAppearances) appearance.Card = null;
            if (completionAppearances.Count == 0 && completionTimer != null) completionTimer.Stop();
        }

        void TrackCompletionCard(Entry entry, FrameworkElement card, int colorIndex) {
            var appearance = completionAppearances.FirstOrDefault(x => x.Matches(entry));
            if (appearance == null) return;
            appearance.Card = card; appearance.ColorIndex = colorIndex;
        }

        void ExpireCompletionFeedback() {
            long now = Stopwatch.GetTimestamp();
            var expired = completionAppearances.Where(x => x.Until <= now).ToList();
            if (expired.Count == 0) return;
            foreach (var appearance in expired) completionAppearances.Remove(appearance);
            if (completionAppearances.Count == 0) completionTimer.Stop();
            // Remove only completed siblings. Rebuilding the board can disturb
            // caret/selection, IME or a resize/drag that starts during feedback.
            foreach (var appearance in expired) {
                var parent = appearance.Card == null ? null : VisualTreeHelper.GetParent(appearance.Card) as Panel;
                if (parent == null) continue;
                parent.Children.Remove(appearance.Card);
                int rowIndex = 0;
                foreach (var card in parent.Children.OfType<Border>()) {
                    card.Background = Theme.CardTint(appearance.ColorIndex, rowIndex);
                    RenameEmptyCompletionSibling(card, rowIndex + 1, GroupLabel(appearance.Group));
                    rowIndex++;
                }
            }
        }

        static void RenameEmptyCompletionSibling(DependencyObject node, int position, string group) {
            string name = AutomationProperties.GetName(node);
            if (name.StartsWith("Edit empty item ", StringComparison.Ordinal)) AutomationProperties.SetName(node, "Edit empty item " + position + " in " + group);
            else if (name.StartsWith("Remove empty item ", StringComparison.Ordinal)) AutomationProperties.SetName(node, "Remove empty item " + position + " in " + group);
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) RenameEmptyCompletionSibling(VisualTreeHelper.GetChild(node, i), position, group);
        }
    }
}
