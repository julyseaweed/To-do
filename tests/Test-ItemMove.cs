using System;
using System.IO;
using System.Linq;
using Shike;

static class ItemMoveTests {
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static void Reject<T>(Action action, string name) where T : Exception {
        bool rejected = false; try { action(); } catch (T) { rejected = true; }
        Check(rejected, name);
    }
    static Entry Item(NoteDocument doc, string title, int occurrence = 0) { return doc.Entries.Where(x => x.Title == title).ElementAt(occurrence); }
    static string Order(string text, string group = "Work") { return string.Join("|", NoteDocument.Parse(text).Entries.Where(x => x.Group == group).Select(x => x.Title)); }
    static void DocumentCases() {
        const string text = "# Test\n\n## Work\n- [ ] A\n- [ ] B\n- [ ] C\n- [ ] D\n\n## Other\n- [ ] Other\n";
        var doc = NoteDocument.Parse(text);
        Check(Order(doc.Move(Item(doc, "C"), Item(doc, "A"))) == "C|A|B|D", "Move a middle row before the first row");
        Check(Order(doc.Move(Item(doc, "A"), Item(doc, "D"))) == "B|C|A|D", "Moving downward keeps the intended target after source removal");
        Check(Order(doc.Move(Item(doc, "D"), Item(doc, "A"))) == "D|A|B|C", "Move the last row to the first position");
        Check(Order(doc.Move(Item(doc, "B"), null)) == "A|C|D|B", "A null anchor moves the row to its own column end");
        Check(doc.Move(Item(doc, "B"), Item(doc, "B")) == text && doc.Move(Item(doc, "B"), Item(doc, "C")) == text && doc.Move(Item(doc, "D"), null) == text, "Self, adjacent-before and already-last moves preserve the exact original text");
        Reject<ArgumentNullException>(delegate { doc.Move(null, Item(doc, "A")); }, "Reject an absent source");
        Reject<InvalidOperationException>(delegate { doc.Move(Item(doc, "A"), Item(doc, "Other")); }, "Reject cross-column anchors");

        const string same = "## Work\n- [ ] Same\n- [ ] Middle\n- [ ] Same\n- [ ] Tail\n";
        var duplicates = NoteDocument.Parse(same);
        Check(duplicates.Move(Item(duplicates, "Same", 0), Item(duplicates, "Same", 1)) == "## Work\n- [ ] Middle\n- [ ] Same\n- [ ] Same\n- [ ] Tail\n", "Resolve a duplicate target before removing an earlier identical source");
        Check(duplicates.Move(Item(duplicates, "Same", 1), Item(duplicates, "Same", 0)) == "## Work\n- [ ] Same\n- [ ] Same\n- [ ] Middle\n- [ ] Tail\n", "Moving the second duplicate upward preserves the chosen occurrence");
        var blanks = NoteDocument.Parse("## Work\n- [ ]\n- [ ] Middle\n- [ ]\n- [ ] Tail\n");
        Check(Order(blanks.Move(Item(blanks, "", 1), Item(blanks, "", 0))) == "||Middle|Tail", "Identical blank cards retain distinct source and target positions");
        Check(Order(blanks.Move(Item(blanks, "", 0), null)) == "Middle||Tail|", "An intentional blank card can move to the column end");

        const string rawBlock = "* [ ] [A\\[B\\]](https://example.test/a?q=1)\r\n  >  Keep  spacing  ";
        string watching = "# Test\r\n\r\n## Watching list\r\n" + rawBlock + "\r\n- [ ] Later\r\n\r\n## Work\r\n- [ ] Untouched\r\n";
        var watched = NoteDocument.Parse(watching);
        string moved = watched.Move(watched.Entries[0], null);
        Check(moved == "# Test\r\n\r\n## Watching list\r\n- [ ] Later\r\n" + rawBlock + "\r\n\r\n## Work\r\n- [ ] Untouched\r\n", "Watching moves preserve the exact title/link/note block, bullet syntax, spacing and CRLF");
        var movedWatch = NoteDocument.Parse(moved).Entries.First(x => x.Title == "A[B]");
        Check(movedWatch.Link == "https://example.test/a?q=1" && movedWatch.Note == " Keep  spacing  " && movedWatch.Span == 2, "The moved Watching card reloads with its full link and note attached");
        var noted = NoteDocument.Parse("## Work\n- [ ] Same\n  > First note\n- [ ] Same\n  > Second note\n- [ ] Tail\n");
        moved = noted.Move(noted.Entries[1], noted.Entries[0]);
        Check(NoteDocument.Parse(moved).Entries.Take(2).Select(x => x.Note).SequenceEqual(new[] { "Second note", "First note" }), "Equal titles with different notes move their own note blocks");

        var hidden = NoteDocument.Parse("## Work\n- [ ] First\n* [X] Hidden\n  > Keep completed\n- [ ] Last\n- [x] Hidden end\n");
        moved = hidden.Move(Item(hidden, "First"), null);
        Check(Order(moved) == "Hidden|Last|Hidden end|First" && NoteDocument.Parse(moved).Entries.Count(x => x.Done) == 2 && moved.Contains("* [X] Hidden\n  > Keep completed"), "Appending includes hidden completed entries without altering their flags or notes");
        moved = hidden.Move(Item(hidden, "Last"), Item(hidden, "First"));
        Check(Order(moved) == "Last|First|Hidden|Hidden end", "Moving visible rows preserves the relative order of hidden completed entries");

        var repeated = NoteDocument.Parse("## Work\n- [ ] First\n\n## Other\n- [ ] Other\n\n## Work\n- [ ] Last\n");
        Check(repeated.Move(Item(repeated, "First"), null) == "## Work\n\n## Other\n- [ ] Other\n\n## Work\n- [ ] Last\n- [ ] First\n", "A repeated topic heading still appends after the last row of the logical column");
        var fenced = NoteDocument.Parse("## Work\n- [ ] First\n```md\n## Fake\n- [ ] Example\n```\n- [ ] Last\n");
        moved = fenced.Move(Item(fenced, "Last"), Item(fenced, "First"));
        Check(moved.Contains("```md\n## Fake\n- [ ] Example\n```") && NoteDocument.Parse(moved).Entries.Count == 2 && !NoteDocument.Parse(moved).Groups.Contains("Fake"), "Reordering preserves fenced Markdown examples and ignores their false rows and headings");
        var noTerminator = NoteDocument.Parse("## Work\n- [ ] First\n- [ ] Last");
        Check(noTerminator.Move(Item(noTerminator, "First"), null) == "## Work\n- [ ] Last\n- [ ] First", "A document without a final newline retains that exact convention");
    }
    static void ConflictCases() {
        var old = NoteDocument.Parse("## Work\n- [ ] Source\n  > Keep note\n- [ ] Anchor\n- [ ] Last\n");
        var source = Item(old, "Source"); var anchor = Item(old, "Anchor");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(old.Text.Replace("Source", "Externally renamed")).Move(source, anchor); }, "Reject a source renamed externally");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(old.Text.Replace("Keep note", "Changed note")).Move(source, anchor); }, "Reject a source whose attached note changed externally");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(old.Text.Replace("- [ ] Source", "- [x] Source")).Move(source, anchor); }, "Reject a source completed externally");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(old.Text.Replace("- [ ] Anchor\n", "")).Move(source, anchor); }, "Reject a target deleted externally");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(old.Text.Replace("Anchor", "Changed target")).Move(source, anchor); }, "Reject a target edited externally");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## Other\n- [ ] External\n").Move(source, null); }, "Reject a deleted source topic rather than resurrecting it");
        var duplicates = NoteDocument.Parse("## Work\n- [ ] Same\n- [ ] Between\n- [ ] Same\n- [ ] Last\n");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## Work\n- [ ] Same\n- [ ] Between\n- [ ] Last\n").Move(duplicates.Entries[2], duplicates.Entries[1]); }, "Reject an ambiguous duplicate source after another duplicate disappears");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(duplicates.Text + "- [ ] Same\n").Move(duplicates.Entries[1], duplicates.Entries[2]); }, "Reject an ambiguous duplicate target after another duplicate appears");
        var latest = NoteDocument.Parse("External preface\n" + old.Text + "- [ ] Unrelated addition\n");
        string moved = latest.Move(source, Item(old, "Last"));
        Check(moved.StartsWith("External preface\n") && Order(moved) == "Anchor|Source|Last|Unrelated addition", "Unrelated external edits and line shifts survive a move with unchanged identities");
        Check(NoteDocument.Parse(moved).Entries.First(x => x.Title == "Source").Note == "Keep note", "A safely resolved stale source still carries its note with it");
    }
    static void StoreCases(string output) {
        var store = new NoteStore(Path.Combine(output, "vault"), Path.Combine(output, "backups")); store.Initialize();
        const string original = "# Test\r\n\r\n## Watching list\r\n- [ ] Watch\r\n\r\n## Work\r\n- [ ] First\r\n- [ ] Second\r\n- [ ] Last\r\n";
        File.WriteAllText(store.CurrentPath, original);
        var snapshot = store.Load();
        string external = "External preface\r\n" + original; File.WriteAllText(store.CurrentPath, external);
        NoteDocument moved;
        string before = store.Move(Item(snapshot, "Last"), Item(snapshot, "First"), out moved);
        Check(before == external && moved != null && moved.Text == File.ReadAllText(store.CurrentPath) && Order(moved.Text) == "Last|First|Second", "The store atomically persists a move and returns the exact pre/post snapshots for Undo");
        Check(File.ReadAllText(Path.Combine(store.BackupDirectory, "当前.last-good.md")) == external, "The move backup preserves the latest external edits");
        var reopened = new NoteStore(store.Vault, store.BackupDirectory);
        Check(reopened.Load().Text == moved.Text, "Reopening the store preserves the reordered cards");
        store.Save(moved.Text, before);
        Check(store.Load().Text == external, "Undo restores the exact previous order and external preface");

        var current = store.Load(); DateTime sentinel = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(store.CurrentPath, sentinel);
        before = store.Move(Item(current, "Last"), null, out moved);
        Check(before == external && moved.Text == external && File.GetLastWriteTimeUtc(store.CurrentPath) == sentinel, "An unchanged end drop performs no disk write");
        string changed = external.Replace("First", "Externally renamed"); File.WriteAllText(store.CurrentPath, changed);
        moved = current;
        Reject<InvalidOperationException>(delegate { store.Move(Item(current, "First"), Item(current, "Last"), out moved); }, "A conflicting store move reports failure");
        Check(moved == null && File.ReadAllText(store.CurrentPath) == changed, "Failed moves return no committed document and leave external content intact");

        current = store.Load(); before = store.Move(Item(current, "Last"), Item(current, "Second"), out moved);
        string afterMoveExternal = moved.Text + "Outside edit after move\r\n"; File.WriteAllText(store.CurrentPath, afterMoveExternal);
        Reject<InvalidOperationException>(delegate { store.Save(moved.Text, before); }, "Undo refuses to overwrite an external edit made after a reorder");
        Check(File.ReadAllText(store.CurrentPath) == afterMoveExternal, "Rejected Undo preserves the newer external note");
        Check(!Directory.GetFiles(store.Vault, "*.tmp").Any(), "Move and conflict paths leave no temporary note files");
    }
    static int Main(string[] args) {
        try { DocumentCases(); ConflictCases(); StoreCases(Path.GetFullPath(args[0])); Console.WriteLine(checks + " item move checks passed."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
