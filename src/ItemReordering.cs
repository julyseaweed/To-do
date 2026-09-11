using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shike {
    public sealed partial class MainWindow {
        static readonly DependencyProperty ReorderEntryProperty = DependencyProperty.RegisterAttached("ReorderEntry", typeof(Entry), typeof(MainWindow));
        static readonly DependencyPropertyDescriptor ItemBackgroundDescriptor = DependencyPropertyDescriptor.FromProperty(Border.BackgroundProperty, typeof(Border));
        sealed class ItemReorderSession {
            public Entry Item, Before;
            public Border Card, Line;
            public ScrollViewer List;
            public Panel Rows;
            public Canvas Overlay;
            public Point PressedAt, ThresholdAt;
            public double GrabY, OriginalOpacity;
            public Transform OriginalTransform;
            public Brush OriginalBackground, LiftedBackground;
            public EventHandler BackgroundChanged;
            public TranslateTransform Motion;
            public Cursor OriginalCursor;
            public bool Started, CanDrop;
        }
        ItemReorderSession itemReorder;
        DispatcherTimer itemReorderTimer;
        bool changingItemCapture;
        long itemReorderTick;
        bool IsItemReordering { get { return itemReorder != null; } }

        void EnableItemReordering() {
            PreviewMouseMove += delegate(object sender, MouseEventArgs e) {
                if (itemReorder == null) return;
                if (e.LeftButton != MouseButtonState.Pressed) { CancelItemReordering(); return; }
                Point point = e.GetPosition(groups);
                if (!itemReorder.Started) {
                    if (!PassedItemDragThreshold(itemReorder.ThresholdAt, e.GetPosition(this))) return;
                    if (!StartItemReordering()) return;
                }
                UpdateItemReordering(point); e.Handled = true;
            };
            PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) {
                if (itemReorder == null) return;
                var session = itemReorder;
                if (session.Started) UpdateItemReordering(e.GetPosition(groups));
                bool move = session.Started && session.CanDrop;
                StopItemReordering(!session.Started);
                if (!session.Started) return;
                e.Handled = true;
                if (move) MoveItemBefore(session.Item, session.Before);
            };
            LostMouseCapture += delegate {
                if (!changingItemCapture && itemReorder != null && itemReorder.Started && Mouse.Captured != itemReorder.Card) CancelItemReordering();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (itemReorder == null || e.Key != Key.Escape) return;
                CancelItemReordering(); e.Handled = true;
            };
            PreviewMouseRightButtonDown += delegate { CancelItemReordering(); };
            SizeChanged += delegate { CancelItemReordering(); };
            Deactivated += delegate { CancelItemReordering(); };
            Closed += delegate { CancelItemReordering(); };
        }

        void AttachItemReordering(Border card, Entry item) {
            card.SetValue(ReorderEntryProperty, item);
            if (item == null || item.Done) return;
            card.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.Handled || e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None || !ItemDragSurface(e.OriginalSource as DependencyObject, card)) return;
                CancelItemReordering();
                itemReorder = new ItemReorderSession { Item = item, Card = card, PressedAt = e.GetPosition(groups), ThresholdAt = e.GetPosition(this) };
                // Saved text keeps its normal Button press and click. Empty
                // card space has no control to capture the pending pointer.
                if (!HasItemTextButton(e.OriginalSource as DependencyObject, card)) {
                    changingItemCapture = true;
                    try { if (!Mouse.Capture(card, CaptureMode.Element)) itemReorder = null; }
                    finally { changingItemCapture = false; }
                    e.Handled = itemReorder != null;
                }
            };
        }
        static DependencyObject ItemParent(DependencyObject node) {
            var visual = node as Visual;
            return visual != null ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        static bool HasItemTextButton(DependencyObject node, Border card) {
            for (; node != null && node != card; node = ItemParent(node)) {
                var button = node as Button;
                if (button == null) continue;
                var field = button.Content as TextBox;
                return field != null && field.IsReadOnly && !field.Focusable;
            }
            return false;
        }
        static bool ItemDragSurface(DependencyObject node, Border card) {
            for (; node != null && node != card; node = ItemParent(node)) {
                if (node is TextBox) return false;
                var button = node as ButtonBase;
                if (button != null) {
                    var field = button.Content as TextBox;
                    return button is Button && field != null && field.IsReadOnly && !field.Focusable;
                }
                if (node is ScrollBar || node is Thumb) return false;
            }
            return node == card;
        }
        static bool PassedItemDragThreshold(Point start, Point current) {
            return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        bool PrepareItemReordering() {
            var session = itemReorder;
            // Committing an editor can rebuild cards. Detach the pending
            // gesture, then find the same saved entry in the new visual tree.
            itemReorder = null;
            var draft = inline;
            bool editingSource = draft != null && !draft.IsTopic && ReferenceEquals(draft.Original, session.Item);
            Entry saved; string savedGroup;
            if (!CommitInline(out saved, out savedGroup)) { ReleaseItemCapture(session); return false; }
            if (editingSource && saved != null) session.Item = saved;
            else if (session.Item.Group == renamedFrom) session.Item.Group = renamedTo;
            if (draft != null) Render();
            session.Item = CurrentEntry(session.Item);
            ScrollViewer list;
            if (session.Item.Done || !document.Entries.Contains(session.Item) || !columnScrolls.TryGetValue(session.Item.Group, out list)) { ReleaseItemCapture(session); return false; }
            var rows = list.Content as Panel;
            var card = rows == null ? null : rows.Children.OfType<Border>().FirstOrDefault(x => ReferenceEquals(x.GetValue(ReorderEntryProperty), session.Item));
            if (card == null) { ReleaseItemCapture(session); return false; }
            list.UpdateLayout();
            session.Card = card; session.List = list; session.Rows = rows;
            session.GrabY = Math.Max(0, Math.Min(card.ActualHeight, session.PressedAt.Y - card.TranslatePoint(new Point(), groups).Y));
            session.OriginalTransform = card.RenderTransform; session.OriginalOpacity = card.Opacity; session.OriginalCursor = card.Cursor;
            session.OriginalBackground = card.Background;
            session.Motion = new TranslateTransform();
            session.Overlay = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            Grid.SetColumnSpan(session.Overlay, 3); Panel.SetZIndex(session.Overlay, 100);
            session.Line = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = Theme.Ink, Opacity = .85 };
            session.Overlay.Children.Add(session.Line); groups.Children.Add(session.Overlay);
            // A completed sibling can disappear and change this row's shade
            // mid-drag. Keep its newest glass brush beneath the opaque lift.
            session.BackgroundChanged = delegate { LiftItemBackground(session); };
            ItemBackgroundDescriptor.AddValueChanged(card, session.BackgroundChanged);
            LiftItemBackground(session);
            card.RenderTransform = session.Motion; card.Opacity = 1; card.Cursor = Cursors.SizeAll;
            Panel.SetZIndex(card, 20);
            session.Started = true; itemReorder = session;
            return true;
        }
        static void LiftItemBackground(ItemReorderSession session) {
            Brush background = session.Card.Background;
            if (ReferenceEquals(background, session.LiftedBackground)) return;
            session.OriginalBackground = background;
            var gradient = background as GradientBrush;
            if (gradient == null) { session.LiftedBackground = background; return; }
            var lifted = (GradientBrush)gradient.CloneCurrentValue();
            lifted.Opacity = 1;
            foreach (var stop in lifted.GradientStops) stop.Color = Color.FromArgb(255, stop.Color.R, stop.Color.G, stop.Color.B);
            lifted.Freeze();
            // Set the identity first: assigning Background raises the same
            // notification again, which must leave the restoration brush alone.
            session.LiftedBackground = lifted; session.Card.Background = lifted;
        }
        bool StartItemReordering() {
            if (!PrepareItemReordering()) return false;
            var session = itemReorder;
            if (Mouse.LeftButton != MouseButtonState.Pressed || !IsActive) { CancelItemReordering(); return false; }
            changingItemCapture = true;
            try { if (!Mouse.Capture(session.Card, CaptureMode.Element)) { CancelItemReordering(); return false; } }
            finally { changingItemCapture = false; }
            if (itemReorderTimer == null) {
                itemReorderTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
                itemReorderTimer.Tick += delegate {
                    if (itemReorder == null || !itemReorder.Started) { itemReorderTimer.Stop(); return; }
                    if (Mouse.LeftButton != MouseButtonState.Pressed) { CancelItemReordering(); return; }
                    long now = Stopwatch.GetTimestamp();
                    double elapsed = Math.Min(.05, (now - itemReorderTick) / (double)Stopwatch.Frequency); itemReorderTick = now;
                    Point point = Mouse.GetPosition(groups);
                    AutoScrollItemReordering(point, elapsed); UpdateItemReordering(point);
                };
            }
            itemReorderTick = Stopwatch.GetTimestamp(); itemReorderTimer.Start();
            return true;
        }
        Rect ItemReorderBounds(ItemReorderSession session) {
            var bounds = new Rect(session.List.TranslatePoint(new Point(), groups), new Size(session.List.ViewportWidth, session.List.ViewportHeight));
            if (session.Item.Group != NoteStore.ReadingGroup) {
                var topics = new Rect(scroll.TranslatePoint(new Point(), groups), new Size(scroll.ViewportWidth, scroll.ViewportHeight));
                bounds.Intersect(topics);
            }
            return bounds;
        }
        void UpdateItemReordering(Point point) {
            var session = itemReorder;
            if (session == null || !session.Started) return;
            Point local = groups.TranslatePoint(point, session.List);
            Point origin = session.Card.TranslatePoint(new Point(), session.List);
            session.Motion.Y += local.Y - session.GrabY - origin.Y;
            Rect visible = ItemReorderBounds(session);
            session.CanDrop = !visible.IsEmpty && visible.Contains(point);
            session.Card.Cursor = session.CanDrop ? Cursors.SizeAll : Cursors.No;
            session.Line.Visibility = session.CanDrop ? Visibility.Visible : Visibility.Collapsed;
            if (!session.CanDrop) return;
            session.Before = null;
            double lineY = 0;
            foreach (var card in session.Rows.Children.OfType<Border>()) {
                var entry = card.GetValue(ReorderEntryProperty) as Entry;
                if (entry == null || entry.Done || ReferenceEquals(entry, session.Item)) continue;
                double top = card.TranslatePoint(new Point(), session.List).Y;
                if (local.Y < top + card.ActualHeight / 2) { session.Before = entry; lineY = top - 3; break; }
                lineY = top + card.ActualHeight + 3;
            }
            lineY = Math.Max(1, Math.Min(session.List.ViewportHeight - 3, lineY));
            Point marker = session.List.TranslatePoint(new Point(0, lineY), groups);
            session.Line.Width = Math.Max(0, visible.Width - 12);
            Canvas.SetLeft(session.Line, visible.Left + 6); Canvas.SetTop(session.Line, Math.Max(visible.Top + 1, Math.Min(visible.Bottom - 3, marker.Y)));
        }
        void AutoScrollItemReordering(Point point, double seconds) {
            var session = itemReorder;
            if (session == null || !session.Started || session.List.ScrollableHeight <= 0) return;
            Point local = groups.TranslatePoint(point, session.List);
            Rect visible = ItemReorderBounds(session);
            if (visible.IsEmpty || point.X < visible.Left || point.X > visible.Right || point.Y < visible.Top - 36 || point.Y > visible.Bottom + 36) return;
            const double edge = 36;
            double speed = local.Y < edge ? -Math.Min(1, (edge - local.Y) / edge) : local.Y > session.List.ViewportHeight - edge ? Math.Min(1, (local.Y - session.List.ViewportHeight + edge) / edge) : 0;
            if (speed == 0) return;
            session.List.ScrollToVerticalOffset(session.List.VerticalOffset + speed * 420 * seconds);
            session.List.UpdateLayout();
        }
        void ReleaseItemCapture(ItemReorderSession session, bool preserveTextClick = false) {
            if (session == null) return;
            var captured = Mouse.Captured as DependencyObject;
            if (preserveTextClick && captured != session.Card) return;
            for (; captured != null && captured != session.Card; captured = ItemParent(captured)) { }
            if (captured == null) return;
            changingItemCapture = true;
            try { Mouse.Capture(null); } finally { changingItemCapture = false; }
        }
        void CancelItemReordering() { StopItemReordering(false); }
        void StopItemReordering(bool preserveTextClick) {
            var session = itemReorder; itemReorder = null;
            if (itemReorderTimer != null) itemReorderTimer.Stop();
            if (session == null) return;
            if (session.Started) {
                ItemBackgroundDescriptor.RemoveValueChanged(session.Card, session.BackgroundChanged);
                session.BackgroundChanged = null;
                session.Card.RenderTransform = session.OriginalTransform; session.Card.Opacity = session.OriginalOpacity; session.Card.Cursor = session.OriginalCursor;
                session.Card.Background = session.OriginalBackground;
                Panel.SetZIndex(session.Card, 0); groups.Children.Remove(session.Overlay);
            }
            ReleaseItemCapture(session, preserveTextClick);
        }
        bool MoveItemBefore(Entry item, Entry before) {
            var visible = document.Entries.Where(x => x.Group == item.Group && !x.Done).ToList();
            int source = visible.IndexOf(item);
            if (source < 0 || before == item || (before == null ? source == visible.Count - 1 : source + 1 < visible.Count && visible[source + 1] == before)) return false;
            try {
                NoteDocument moved;
                string previous = Store.Move(item, before, out moved);
                if (previous == moved.Text) return false;
                document = moved; RememberUndo(previous, moved.Text); Render(); Notify("Moved", true, false); return true;
            } catch (Exception ex) {
                try { Refresh(); } catch { }
                Notify(ex.Message, false, true); return false;
            }
        }
    }
}
