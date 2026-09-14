using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shike {
    public sealed partial class MainWindow {
        sealed class InlineDraft {
            public bool IsTopic, ShowLink, Composing;
            public string Group, Text = "", Link = "", Error = "";
            public Entry Original, Following;
            public TextBox Input, LinkInput;
            public TextBlock ErrorLabel;
            public FrameworkElement Host;
            public DispatcherTimer BlurTimer;
        }
        InlineDraft inline;
        InlineDraft inlineRemovalUndo;
        Key inlineRemovalKey;
        string renamedFrom, renamedTo;

        void BeginTopic(string name) {
            Entry saved; string group;
            if (!CommitInline(out saved, out group)) return;
            if (collapsed) ToggleCollapsed();
            if (name != null && name == renamedFrom) name = renamedTo;
            if (name != null && !document.Groups.Contains(name)) { Render(); Notify("This topic changed elsewhere. Choose a topic again.", false, true); return; }
            if (name == null) {
                try {
                    var latest = Store.Load(); name = NoteDocument.NewEmptyGroup();
                    string updated = latest.ChangeGroup(null, name); Store.Save(latest.Text, updated);
                    RememberUndo(latest.Text, updated); document = NoteDocument.Parse(updated);
                } catch (Exception ex) { Notify(ex.Message, false, true); return; }
            }
            inline = new InlineDraft { IsTopic = true, Group = name, Text = name == NoteStore.ReadingGroup ? WatchingTitle : NoteDocument.GroupTitle(name) };
            Render(); FocusInline();
        }
        void BeginItem(Entry item, string group, bool link) {
            Entry saved; string savedGroup;
            if (!CommitInline(out saved, out savedGroup)) return;
            if (collapsed) ToggleCollapsed();
            if (group == renamedFrom) group = renamedTo;
            if (group == null) { Render(); return; }
            if (item != null) item = CurrentEntry(item);
            if (item != null && item.Done) { Render(); return; }
            if (item != null && !document.Entries.Contains(item)) { Render(); Notify("This item changed elsewhere. Select it again.", false, true); return; }
            if (!document.Groups.Contains(group)) { Render(); Notify("This topic changed elsewhere. Choose a topic again.", false, true); return; }
            inline = new InlineDraft { Group = group, Original = item, Text = item == null ? "" : item.Title, Link = item == null ? "" : item.Link, ShowLink = link || group == NoteStore.ReadingGroup };
            if (item == null && CommitInline(out saved, out savedGroup))
                inline = new InlineDraft { Group = savedGroup, Original = saved, ShowLink = link || savedGroup == NoteStore.ReadingGroup };
            Render(); FocusInline(link);
        }
        Entry CurrentEntry(Entry entry) {
            var matches = document.Entries.Where(x => x.Group == entry.Group && x.Raw == entry.Raw).ToList();
            return matches.Count == entry.Duplicates && entry.Occurrence < matches.Count ? matches[entry.Occurrence] : entry;
        }
        void EditLink(Entry item) { BeginItem(item, item.Group, true); }

        TextBox InlineInput(InlineDraft draft, bool link, double size, Brush ink, string placeholder) {
            var input = TextField(link ? draft.Link : draft.Text, size, ink, draft.IsTopic ? TextFieldRole.Heading : link ? TextFieldRole.Link : TextFieldRole.Body, false);
            input.MaxLength = draft.IsTopic ? 60 : link ? 2000 : 400;
            AutomationProperties.SetName(input, link ? "Item link" : draft.IsTopic ? "Topic name" : "Item text");
            AutomationProperties.SetHelpText(input, placeholder);
            if (link) draft.LinkInput = input; else draft.Input = input;
            input.TextChanged += delegate {
                inlineRemovalUndo = null;
                if (link) draft.Link = input.Text; else draft.Text = input.Text;
                draft.Error = ""; if (draft.ErrorLabel != null) draft.ErrorLabel.Visibility = Visibility.Collapsed;
            };
            TextCompositionManager.AddPreviewTextInputStartHandler(input, delegate { draft.Composing = true; });
            TextCompositionManager.AddPreviewTextInputHandler(input, delegate {
                draft.Composing = false;
                if (!IsActive) Dispatcher.BeginInvoke(new Action(delegate { if (inline == draft) FinishInline(false); }), DispatcherPriority.Background);
            });
            input.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                // Candidate Enter/Escape belong to the IME, not the draft.
                if (e.Key == Key.ImeProcessed || draft.Composing) return;
                if (TryDeleteEmptyInline(draft, e, link)) e.Handled = true;
                else if (e.Key == Key.Escape) { e.Handled = true; CancelInline(); }
                else if (e.Key == Key.Enter) { e.Handled = true; FinishInline(!link || draft.Group == NoteStore.ReadingGroup); }
            };
            input.LostKeyboardFocus += delegate { ScheduleInlineBlur(draft); };
            return input;
        }
        bool TryDeleteEmptyInline(InlineDraft draft, KeyEventArgs e, bool link) {
            if (e.Key != Key.Back && e.Key != Key.Delete) return false;
            // Holding a key must not spill into the neighboring field after
            // deleting this one. A fresh press re-enables ordinary editing.
            if (e.IsRepeat && inlineRemovalKey == e.Key) return true;
            if (!e.IsRepeat) inlineRemovalKey = Key.None;
            if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || inline != draft || draft.Composing ||
                !string.IsNullOrEmpty(draft.Text)) return false;
            if (draft.IsTopic) {
                if (draft.Group == NoteStore.ReadingGroup) return false;
            } else if (!string.IsNullOrEmpty(draft.Link) ||
                (draft.Original != null && !string.IsNullOrWhiteSpace(draft.Original.Note))) return false;
            if (DeleteEmptyInline(draft, e.Key == Key.Back, link)) inlineRemovalKey = e.Key;
            return true;
        }
        bool DeleteEmptyInline(InlineDraft draft, bool backwards, bool link) {
            try {
                if (draft.IsTopic) {
                    var latestTopic = Store.Load();
                    if (!latestTopic.GroupSections(draft.Group).SequenceEqual(document.GroupSections(draft.Group)))
                        throw new InvalidOperationException("This topic changed elsewhere.");
                    // Delete before committing the cleared title so a single
                    // Undo restores its original name and all of its contents.
                    string updatedTopic = latestTopic.ChangeGroup(draft.Group, null);
                    Store.Save(latestTopic.Text, updatedTopic); RememberUndo(latestTopic.Text, updatedTopic);
                    document = NoteDocument.Parse(updatedTopic); inline = null; inlineRemovalUndo = null;
                    Render(); Notify("Deleted", true, false); Focus(); return true;
                }
                if (draft.Original == null) { inline = null; inlineRemovalUndo = null; Render(); Focus(); return true; }
                var latest = Store.Load();
                // Remove the original in one optimistic write. Saving an
                // intermediate blank would lose the original text from Undo.
                string updated = latest.Change(draft.Original, null, draft.Group);
                var found = latest.Entries.Where(x => x.Group == draft.Group && x.Raw == draft.Original.Raw).ElementAt(draft.Original.Occurrence);
                var visible = latest.Entries.Where(x => x.Group == draft.Group && !x.Done).ToList();
                var before = visible.LastOrDefault(x => x.Line < found.Line);
                var after = visible.FirstOrDefault(x => x.Line > found.Line);
                var neighbor = backwards ? before ?? after : after ?? before;
                var next = NoteDocument.Parse(updated);
                Entry target = null;
                if (neighbor != null) {
                    int line = neighbor.Line > found.Line ? neighbor.Line - found.Span : neighbor.Line;
                    target = next.Entries.First(x => x.Line == line);
                }
                Store.Save(latest.Text, updated); RememberUndo(latest.Text, updated);
                document = next; inline = null; inlineRemovalUndo = null;
                if (target != null) {
                    inline = new InlineDraft { Group = target.Group, Original = target, Text = target.Title, Link = target.Link ?? "", ShowLink = draft.ShowLink || target.Group == NoteStore.ReadingGroup };
                    inlineRemovalUndo = inline;
                }
                // Rendering assigns each remaining card its new row index,
                // so all following shades move up with their cards.
                Render(); Notify("Deleted", true, false);
                if (inline != null) FocusInline(link); else Focus();
                return true;
            } catch (Exception ex) {
                ShowInlineError(draft, ex is InvalidOperationException ? "The note changed elsewhere. Your text is kept here. Copy it or press Esc to refresh." : ex.Message);
                return false;
            }
        }
        TextBlock InlineError(InlineDraft draft) {
            draft.ErrorLabel = Theme.Text(draft.Error, 11, Theme.Error); draft.ErrorLabel.TextWrapping = TextWrapping.Wrap;
            draft.ErrorLabel.Margin = new Thickness(0, 4, 0, 2);
            draft.ErrorLabel.Visibility = string.IsNullOrEmpty(draft.Error) ? Visibility.Collapsed : Visibility.Visible;
            AutomationProperties.SetName(draft.ErrorLabel, "Input error"); return draft.ErrorLabel;
        }
        FrameworkElement InlineTopic(InlineDraft draft) {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(InlineInput(draft, false, 19, Theme.Ink, "Topic name"));
            panel.Children.Add(InlineError(draft)); draft.Host = panel; return panel;
        }
        FrameworkElement InlineItem(InlineDraft draft, int colorIndex, int rowIndex) {
            bool watching = draft.Group == NoteStore.ReadingGroup;
            Brush ink = Theme.CardForeground(colorIndex), muted = Theme.CardSecondary(colorIndex);
            var grid = new Grid { MinHeight = watching ? 74 : 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var actionColumn = new ColumnDefinition(); grid.ColumnDefinitions.Add(actionColumn);
            Action updateLinkLayout = delegate {
                bool hasLink = !string.IsNullOrWhiteSpace(draft.Link);
                grid.Margin = new Thickness(7, 0, hasLink ? 5 : 12, 0);
                actionColumn.Width = new GridLength(hasLink ? 27 : 0);
            };
            updateLinkLayout();
            var check = new CheckBox { IsChecked = draft.Original != null && draft.Original.Done };
            Action updateCheckName = delegate {
                bool empty = string.IsNullOrWhiteSpace(draft.Text) && string.IsNullOrWhiteSpace(draft.Link) && (draft.Original == null || string.IsNullOrWhiteSpace(draft.Original.Note));
                AutomationProperties.SetName(check, empty ? "Remove current empty item" : "Complete current item");
            };
            updateCheckName();
            check.Click += delegate {
                bool done = check.IsChecked == true; Entry saved; string group;
                if (!CommitInline(out saved, out group)) { check.IsChecked = !done; return; }
                if (saved == null) { Render(); return; }
                if (string.IsNullOrWhiteSpace(saved.Title) && string.IsNullOrWhiteSpace(saved.Link) && string.IsNullOrWhiteSpace(saved.Note)) { Change(saved, null, group, "Deleted"); return; }
                var changed = Copy(saved); changed.Done = done; Change(saved, changed, group, done ? "Completed" : "Restored");
            };
            grid.Children.Add(check);
            var text = ItemTextPanel();
            var title = InlineInput(draft, false, 15.5, ink, "Item text");
            title.TextChanged += delegate { updateCheckName(); };
            text.Children.Add(title);
            if (draft.ShowLink || watching) {
                if (watching) text.Children.Add(Theme.CardDivider());
                var link = InlineInput(draft, true, 11.5, ink, "Item link"); link.Margin = watching ? new Thickness(0) : new Thickness(0, 5, 0, 1);
                link.TextChanged += delegate { updateCheckName(); updateLinkLayout(); };
                text.Children.Add(link);
            }
            if (draft.Original != null && !string.IsNullOrWhiteSpace(draft.Original.Note)) {
                var note = Theme.Text(draft.Original.Note, 11, muted); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 4, 0, 0); text.Children.Add(note);
            }
            var error = InlineError(draft); error.Foreground = ink; text.Children.Add(error);
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            var pill = new Border { Child = grid, CornerRadius = new CornerRadius(24), Background = Theme.CardTint(colorIndex, rowIndex), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 6) };
            pill.Resources["Ink"] = ink; pill.Resources["Ring"] = muted;
            draft.Host = pill; AttachItemReordering(pill, draft.Original); return pill;
        }
        void FocusInline(bool link = false) {
            var draft = inline;
            int? clickedCaret = clickedTextCaret;
            Dispatcher.BeginInvoke(new Action(delegate {
                if (draft == null || inline != draft) return;
                var input = link && draft.LinkInput != null ? draft.LinkInput : draft.Input;
                if (input == null) return;
                if (clickedCaret.HasValue) {
                    // A pointer already identifies visible text. Keep that
                    // insertion point instead of scrolling to the row's end.
                    input.CaretIndex = Math.Min(clickedCaret.Value, input.Text.Length);
                    input.Focus();
                } else {
                    input.BringIntoView(); input.Focus(); input.CaretIndex = input.Text.Length;
                }
            }), DispatcherPriority.Input);
        }
        void ScheduleInlineBlur(InlineDraft draft) {
            if (draft.BlurTimer != null) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            draft.BlurTimer = timer;
            timer.Tick += delegate {
                if (inline != draft || draft.Host.IsKeyboardFocusWithin) { timer.Stop(); draft.BlurTimer = null; return; }
                // Wait for the click to reach its target before rebuilding rows.
                // This preserves checkbox, plus-button and next-row clicks.
                // Keep the pending blur alive while the IME finishes its text.
                if (draft.Composing || Mouse.LeftButton == MouseButtonState.Pressed || Keyboard.FocusedElement is MenuItem) return;
                timer.Stop(); draft.BlurTimer = null;
                FinishInline(false);
            };
            timer.Start();
        }
        void CancelInline() {
            try { var latest = Store.Load(); inline = null; document = latest; Render(); Focus(); }
            catch (Exception ex) { ShowInlineError(inline, ex.Message); }
        }
        bool FinishInline(bool advance) {
            var draft = inline; if (draft == null) return true;
            Entry saved; string group;
            if (!CommitInline(out saved, out group, advance)) return false;
            if (draft.Following != null) {
                inline = new InlineDraft { Group = group, Original = draft.Following, ShowLink = group == NoteStore.ReadingGroup };
                Render(); FocusInline();
            } else Render();
            return true;
        }
        bool CommitInline(out Entry saved, out string group, bool advance = false) {
            saved = null; group = null; renamedFrom = renamedTo = null;
            var draft = inline; if (draft == null) return true;
            if (draft.Composing) return false;
            if (draft.IsTopic && draft.Group == NoteStore.ReadingGroup) return CommitWatchingTitle(draft, out saved, out group, advance);
            group = draft.Group;
            string value = draft.Text.Trim();
            try {
                var latest = Store.Load(); string updated = latest.Text;
                int line = 0, removed = 0, added = 0;
                if (draft.IsTopic) {
                    group = value.Length > 0 ? value : NoteDocument.IsEmptyGroup(draft.Group) ? draft.Group : NoteDocument.NewEmptyGroup();
                    if (group != draft.Group) updated = latest.ChangeGroup(draft.Group, group);
                    else if (!latest.Groups.Contains(group)) { document = latest; group = null; inline = null; return true; }
                } else {
                    if (!latest.Groups.Contains(group)) throw new InvalidOperationException("This topic changed elsewhere. Your text is still here; copy it or press Esc to refresh.");
                    var replacement = draft.Original == null ? new Entry { Group = group, Done = false, Note = "" } : Copy(draft.Original);
                    replacement.Title = value; replacement.Link = (draft.Link ?? "").Trim();
                    Uri uri;
                    if ((draft.Original == null || string.IsNullOrWhiteSpace(draft.Original.Title)) && replacement.Link.Length == 0 && Uri.TryCreate(value, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http")) replacement.Link = value;
                    NoteStore.Validate(replacement);
                    if (draft.Original == null) {
                        updated = latest.InsertAfter(null, replacement, group, out saved);
                        line = saved.Line; added = saved.Span;
                    } else {
                        var original = draft.Original;
                        if (replacement.Title == original.Title && replacement.Link == (original.Link ?? "") && replacement.Note == (original.Note ?? "") && replacement.Done == original.Done) {
                            var matches = latest.Entries.Where(x => x.Group == original.Group && x.Raw == original.Raw).ToList();
                            if (matches.Count != original.Duplicates || original.Occurrence >= matches.Count) {
                                document = latest; inline = null; return true;
                            }
                            saved = matches[original.Occurrence];
                        } else {
                            updated = latest.Change(original, replacement, group);
                            var found = latest.Entries.Where(x => x.Group == original.Group && x.Raw == original.Raw).ElementAt(original.Occurrence);
                            saved = NoteDocument.Parse(updated).Entries.First(x => x.Line == found.Line);
                            line = found.Line; removed = found.Span; added = saved.Span;
                        }
                    }
                }
                var staged = NoteDocument.Parse(updated);
                Entry following = null;
                if (advance) {
                    int anchorLine = saved == null ? -1 : saved.Line;
                    Entry anchor = anchorLine < 0 ? null : staged.Entries.First(x => x.Line == anchorLine);
                    updated = staged.InsertAfter(anchor, new Entry { Group = group, Title = "", Link = "", Note = "" }, group, out following);
                }
                // Enter saves the current edit and the following blank as one
                // transaction, so a failed save cannot leave half an operation.
                if (updated != latest.Text) { Store.Save(latest.Text, updated); RememberUndo(latest.Text, updated); }
                AdoptInlineDocument(latest, staged, draft.Original, saved, line, removed, added);
                if (advance) {
                    var final = NoteDocument.Parse(updated);
                    AdoptInlineDocument(staged, final, null, null, following.Line, 0, following.Span);
                    draft.Following = final.Entries.First(x => x.Line == following.Line);
                }
                if (saved != null) { int savedLine = saved.Line; saved = document.Entries.First(x => x.Line == savedLine); }
                if (draft.IsTopic && group != draft.Group) { renamedFrom = draft.Group; renamedTo = group; }
                inline = null; return true;
            } catch (Exception ex) {
                ShowInlineError(draft, ex is InvalidOperationException ? "The note changed elsewhere. Your text is kept here. Copy it or press Esc to refresh." : ex.Message);
                return false;
            }
        }
        void ShowInlineError(InlineDraft draft, string message) {
            if (draft == null) { Notify(message, false, true); return; }
            draft.Error = message;
            if (draft.ErrorLabel != null) { draft.ErrorLabel.Text = message; draft.ErrorLabel.Visibility = Visibility.Visible; }
        }
        void AdoptInlineDocument(NoteDocument before, NoteDocument after, Entry original, Entry saved, int line, int removed, int added) {
            // Click callbacks may still hold the previous row object. Rebase
            // identities after our own insertion/edit, including duplicate rows.
            var identities = before.Entries.Select(x => new { x.Group, x.Raw, x.Line }).ToList();
            foreach (var entry in document.Entries) {
                Entry replacement = null;
                if (ReferenceEquals(entry, original)) replacement = saved;
                else {
                    var matches = identities.Where(x => x.Group == entry.Group && x.Raw == entry.Raw).ToList();
                    if (matches.Count != entry.Duplicates || entry.Occurrence >= matches.Count) continue;
                    int position = matches[entry.Occurrence].Line;
                    if (position >= line + removed) position += added - removed;
                    replacement = after.Entries.FirstOrDefault(x => x.Line == position && x.Raw == entry.Raw);
                }
                if (replacement == null) continue;
                entry.Group = replacement.Group; entry.Title = replacement.Title; entry.Link = replacement.Link; entry.Note = replacement.Note; entry.Done = replacement.Done;
                entry.Raw = replacement.Raw; entry.Line = replacement.Line; entry.Span = replacement.Span; entry.Occurrence = replacement.Occurrence; entry.Duplicates = replacement.Duplicates;
            }
            document = after;
        }
    }
}
