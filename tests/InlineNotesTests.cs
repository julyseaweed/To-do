using System;
using System.IO;
using System.Linq;
using Shike;

static class InlineNotesTests {
    static int checks;
    static void Check(bool condition, string name) {
        if (!condition) throw new Exception(name);
        checks++; Console.WriteLine("PASS " + name);
    }
    static void Reject<T>(Action action, string name) where T : Exception {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Check(rejected, name);
    }
    static Entry Item(string title) { return new Entry { Group = "Work", Title = title, Link = "", Note = "" }; }
    static string Titles(NoteDocument document) { return string.Join("|", document.Entries.Select(x => x.Title)); }

    static void BlankRows() {
        var blank = Item(""); NoteStore.Validate(blank); NoteStore.Validate(Item(null));
        Check(NoteDocument.Serialize(blank, "\n") == "- [ ]" && NoteDocument.Serialize(Item("  \t"), "\n") == "- [ ]", "Blank rows serialize as a plain Markdown checkbox with no placeholder or hidden text");
        var variants = NoteDocument.Parse("## Work\r\n- [ ]\r\n- [ ] \r\n* [x]\t\r\n- [X]\r\n\r\n \r\n\t\r\n");
        Check(variants.Entries.Count == 4 && variants.Entries.All(x => x.Title == "") && variants.Entries.Count(x => x.Done) == 2, "Parse bare and whitespace-terminated blank checkboxes, including completed rows");
        Check(variants.Entries[0].Raw == "- [ ]" && variants.Entries[1].Raw == "- [ ] " && variants.Newline == "\r\n", "Retain exact external blank-row Markdown and CRLF identity");
        Check(NoteDocument.Parse("## Work\n\n \n\t\n- [ ]word\n").Entries.Count == 0, "Ordinary empty lines and malformed checkbox syntax are not blank entries");
        Check(NoteDocument.Parse("- [ ]\n## Work\n```md\n- [ ]\n```\n~~~\n* [x]\n~~~\n- [ ]\n").Entries.Count == 1, "Ignore blank checkbox examples outside topics and inside either code fence");
        var noted = NoteDocument.Parse("## Work\r\n- [ ]\r\n  > Existing external note\r\n- [ ] Last\r\n");
        Entry inserted;
        string next = noted.InsertAfter(noted.Entries[0], blank, "Work", out inserted);
        Check(noted.Entries[0].Span == 2 && next.Contains("  > Existing external note\r\n- [ ]\r\n- [ ] Last"), "Preserve an external blank anchor's note span when inserting a new blank row");

        var chain = NoteDocument.Parse("## Work\r\n- [ ] First\r\n  > Keep this note\r\n\r\n## To read\r\n");
        Entry anchor = chain.Entries[0];
        for (int i = 0; i < 5; i++) {
            next = chain.InsertAfter(anchor, Item(""), "Work", out inserted);
            chain = NoteDocument.Parse(next); anchor = inserted;
            Check(anchor.Duplicates == i + 1 && anchor.Occurrence == i, "Repeated blank insertion returns the correct duplicate identity at row " + (i + 1));
        }
        Check(chain.Entries.Count == 6 && chain.Entries.Skip(1).All(x => x.Title == "") && chain.Text.Contains("Keep this note\r\n- [ ]\r\n- [ ]"), "Repeated empty Enter-style insertions retain every row, order, and CRLF");
        var secondBlank = chain.Entries[2];
        var filled = NoteDocument.Parse(chain.Change(secondBlank, Item("Filled second blank"), "Work"));
        Check(filled.Entries.Count == 6 && filled.Entries[2].Title == "Filled second blank" && filled.Entries[1].Title == "" && filled.Entries[3].Title == "", "Editing one duplicate blank fills only the intended row");
        var cleared = NoteDocument.Parse(filled.Change(filled.Entries[2], Item(""), "Work"));
        Check(cleared.Text == chain.Text && cleared.Entries.Count == 6, "Clearing a filled title preserves its row and restores standard blank Markdown");
        next = chain.InsertBefore(chain.Entries[2], Item(""), "Work", out inserted);
        Check(NoteDocument.Parse(next).Entries.Count == 7 && inserted.Line == chain.Entries[2].Line && inserted.Occurrence == 1, "Insert a blank before the selected duplicate occurrence");
        var completedBlank = Item(""); completedBlank.Done = true;
        var completed = NoteDocument.Parse(chain.Change(chain.Entries[2], completedBlank, "Work"));
        Check(completed.Entries.Count == 6 && completed.Entries[2].Done && completed.Entries[2].Raw == "- [x]", "A completed blank row remains a standard persisted entry");
        var removed = NoteDocument.Parse(chain.Change(chain.Entries[2], null, "Work"));
        Check(removed.Entries.Count == 5 && removed.Entries[0].Note == "Keep this note", "Delete only the selected blank row without affecting surrounding note spans");
        var duplicateBlanks = NoteDocument.Parse("## Work\n- [ ]\n- [ ]\n");
        var fewer = NoteDocument.Parse("## Work\n- [ ]\n");
        Reject<InvalidOperationException>(delegate { fewer.InsertAfter(duplicateBlanks.Entries[0], Item(""), "Work"); }, "Refuse inserting after an ambiguous blank when another blank disappears");
        Reject<InvalidOperationException>(delegate { fewer.Change(duplicateBlanks.Entries[1], Item("Typed"), "Work"); }, "Refuse editing a blank when its duplicate count changed externally");
        var more = NoteDocument.Parse("## Work\n- [ ]\n- [ ]\n- [ ]\n");
        Reject<InvalidOperationException>(delegate { more.Change(duplicateBlanks.Entries[0], null, "Work"); }, "Refuse deleting an ambiguous blank when an external duplicate appears");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## Work\n- [ ] Filled externally\n- [ ]\n").InsertAfter(duplicateBlanks.Entries[0], Item(""), "Work"); }, "Refuse a blank anchor that was filled elsewhere");
        var linkFirst = Item(""); linkFirst.Link = "https://example.com/article?q=one&lang=en"; linkFirst.Note = "Read this later";
        var linkFirstReloaded = NoteDocument.Parse("## Work\n" + NoteDocument.Serialize(linkFirst, "\n")).Entries.Single();
        Check(linkFirstReloaded.Title == "" && linkFirstReloaded.Link == linkFirst.Link && linkFirstReloaded.Note == linkFirst.Note, "A URL and note round-trip before entering a row title");
        var noteFirst = Item(" "); noteFirst.Note = "A note entered first";
        var noteFirstReloaded = NoteDocument.Parse("## Work\n" + NoteDocument.Serialize(noteFirst, "\n")).Entries.Single();
        Check(noteFirstReloaded.Title == "" && noteFirstReloaded.Link == "" && noteFirstReloaded.Note == noteFirst.Note, "A note alone round-trips without inventing a title");
        Reject<ArgumentException>(delegate { var item = Item(""); item.Link = "javascript:alert(1)"; NoteStore.Validate(item); }, "Title-free rows still reject executable URL schemes");
        Reject<ArgumentException>(delegate { var item = Item(""); item.Note = "First\nSecond"; NoteStore.Validate(item); }, "Title-free notes still reject embedded newlines");
        Reject<ArgumentException>(delegate { var item = Item(""); item.Note = new string('n', 1001); NoteStore.Validate(item); }, "Title-free notes retain the length limit");
        Reject<ArgumentException>(delegate { NoteStore.Validate(Item("\n")); }, "An empty-looking title cannot inject a newline");
        Reject<ArgumentException>(delegate { NoteStore.Validate(Item(new string('x', 401))); }, "Nonempty title length limits still apply");

        string directory = Path.Combine(Path.GetTempPath(), "shike-blank-rows-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var store = new NoteStore(directory, Path.Combine(directory, "backups"));
        string original = "# Current\r\n\r\n## Work\r\n\r\n## To read\r\n"; File.WriteAllText(store.CurrentPath, original);
        string before = store.InsertAfter(null, Item(""), "Work", out inserted);
        Check(before == original && store.Load().Entries.Count == 1 && store.Load().Entries[0].Title == "", "A blank row persists through NoteStore and reload without invented content");
        var staleBlank = inserted; File.AppendAllText(store.CurrentPath, "\r\nExternal paragraph\r\n");
        before = store.Mutate(staleBlank, Item("Filled later"), "Work");
        Check(store.Load().Text.Contains("External paragraph") && store.Load().Entries[0].Title == "Filled later", "Filling a persisted blank preserves independent external edits");
        store.Save(store.Load().Text, before);
        Check(store.Load().Entries[0].Title == "" && store.Load().Text.Contains("External paragraph"), "Undo restores a blank row and retains independent edits");
        before = store.Mutate(store.Load().Entries[0], completedBlank, "Work");
        Check(store.Load().Entries[0].Done && store.Load().Entries[0].Title == "", "Completing a stored blank persists across reload");
        store.Save(store.Load().Text, before);
        before = store.Mutate(store.Load().Entries[0], null, "Work");
        Check(store.Load().Entries.Count == 0, "Deleting a stored blank removes the row");
        store.Save(store.Load().Text, before);
        Check(store.Load().Entries.Count == 1 && !store.Load().Entries[0].Done && store.Load().Entries[0].Title == "", "Undo restores the exact active blank row after deletion");
        Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Blank row operations leave no temporary note files");
    }

    static void EmptyTopics() {
        string first = NoteDocument.NewEmptyGroup(), second = NoteDocument.NewEmptyGroup();
        Check(first != second && NoteDocument.IsEmptyGroup(first) && NoteDocument.IsEmptyGroup(second), "Each unnamed topic receives its own internal identity");
        Check(NoteDocument.GroupTitle(first) == "" && NoteDocument.GroupTitle("Named topic") == "Named topic", "Unnamed topics display no assumed title while ordinary names remain unchanged");
        Check(!NoteDocument.IsEmptyGroup(null) && !NoteDocument.IsEmptyGroup("<!-- shike-topic:not-a-guid -->") && NoteDocument.GroupTitle("<!-- ordinary comment -->") == "<!-- ordinary comment -->", "Only the exact internal unnamed-topic token is hidden");
        var document = NoteDocument.Parse("# Current\r\n\r\n## To read\r\n");
        document = NoteDocument.Parse(document.ChangeGroup(null, first));
        document = NoteDocument.Parse(document.ChangeGroup(null, second));
        Check(document.Groups.Count == 3 && document.Groups.Contains(first) && document.Groups.Contains(second) && document.Groups.Select(NoteDocument.GroupTitle).Count(x => x == "") == 2, "Multiple unnamed topics persist separately immediately after creation");
        Check(document.Text.Contains("## " + first + "\r\n") && NoteDocument.Parse(document.Text).Groups.SequenceEqual(document.Groups), "Unnamed headings round-trip as Markdown comment identities with CRLF");
        Entry inserted;
        document = NoteDocument.Parse(document.InsertAfter(null, new Entry { Group = first, Title = "Keep my item" }, first, out inserted));
        document = NoteDocument.Parse(document.InsertAfter(null, new Entry { Group = second, Title = "" }, second, out inserted));
        Check(document.Entries.Single(x => x.Group == first).Title == "Keep my item" && document.Entries.Single(x => x.Group == second).Title == "", "Named and blank rows stay assigned to the correct unnamed topic");
        document = NoteDocument.Parse(document.ChangeGroup(first, "Named later"));
        Check(!document.Groups.Contains(first) && document.Entries.Single(x => x.Title == "Keep my item").Group == "Named later" && document.Groups.Contains(second), "Naming an unnamed topic preserves its entries and the other unnamed topic");
        string cleared = NoteDocument.NewEmptyGroup();
        document = NoteDocument.Parse(document.ChangeGroup("Named later", cleared));
        Check(document.Groups.Contains(cleared) && NoteDocument.GroupTitle(cleared) == "" && document.Entries.Single(x => x.Title == "Keep my item").Group == cleared, "Clearing a topic name to a fresh identity preserves all its entries");
        Reject<ArgumentException>(delegate { document.ChangeGroup(null, second); }, "Do not create two topics with the same internal identity");
        Reject<ArgumentException>(delegate { document.ChangeGroup(null, ""); }, "The raw empty string is not a topic identity");
        Reject<ArgumentException>(delegate { document.ChangeGroup(null, new string('N', 61)); }, "Normal visible topic names retain the 60-character limit");
        var removed = NoteDocument.Parse(document.ChangeGroup(cleared, null));
        Check(!removed.Groups.Contains(cleared) && removed.Groups.Contains(second) && removed.Entries.Count == 1 && removed.Entries[0].Title == "", "Deleting one unnamed topic preserves a different unnamed topic and its blank row");
    }

    static NoteStore MigrationStore(string source) {
        string directory = Path.Combine(Path.GetTempPath(), "shike-watching-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new NoteStore(directory, Path.Combine(directory, "backups"));
        File.WriteAllText(store.CurrentPath, source); return store;
    }
    static string EntryMetadata(Entry entry) {
        return entry.Title + "\n" + entry.Link + "\n" + entry.Note + "\n" + entry.Done + "\n" + entry.Raw + "\n" + entry.Span + "\n" + entry.Line + "\n" + entry.Occurrence + "\n" + entry.Duplicates;
    }
    static void WatchingListMigration() {
        string prefix = "# Current\r\n\r\nKeep this prose mentioning To read.\r\n```md\r\n## To read\r\n- [ ] Fenced example\r\n```\r\n\r\n## Work\r\n- [ ] Keep working\r\n\r\n";
        string rows = "- [ ]\r\n- [ ]\r\n- [x] [Article](<https://example.com/read?a=1&b=2>)\r\n  > Keep its note\r\n- [ ] [[Folder/Note|Wiki alias]]\r\n- [ ] [](<https://example.com/unnamed>)\r\n  > Metadata entered first\r\n\r\n## Other\r\nKeep this paragraph.\r\n\r\n";
        string source = prefix + "## To read  \r\n" + rows + "## To read\r\n- [ ] Last item";
        string expected = prefix + "## Watching list  \r\n" + rows + "## Watching list\r\n- [ ] Last item";
        var before = NoteDocument.Parse(source);
        var store = MigrationStore(source); store.Initialize(); var migrated = store.Load();
        Check(migrated.Text == expected, "Watching list migration changes only real legacy headings and preserves spacing, CRLF, prose, fences, and missing final newline");
        Check(migrated.Groups.Count(x => x == NoteStore.ReadingGroup) == 1 && !migrated.Groups.Contains("To read"), "Repeated legacy sections migrate into the same Watching list group");
        var oldItems = before.Entries.Where(x => x.Group == "To read").ToList();
        var newItems = migrated.Entries.Where(x => x.Group == NoteStore.ReadingGroup).ToList();
        Check(oldItems.Count == 6 && oldItems.Select(EntryMetadata).SequenceEqual(newItems.Select(EntryMetadata)), "Migration preserves all blank rows, links, wiki links, notes, completion flags, order, spans, and duplicate identities");
        Check(File.ReadAllText(Path.Combine(store.BackupDirectory, "当前.last-good.md")) == source, "Migration backs up the exact previous note");
        store.Initialize(); store.Initialize();
        Check(store.Load().Text == expected && store.Load().Entries.Count == before.Entries.Count, "Reopening after migration creates neither duplicate headings nor duplicate rows");

        string chinese = "# Current\n\n## 打算看\n- [ ]\n- [ ] [Article](<https://example.com/zh>)\n  > 原有附注\n";
        store = MigrationStore(chinese); store.Initialize();
        Check(store.Load().Text == chinese.Replace("## 打算看\n", "## Watching list\n") && store.Load().Entries[1].Note == "原有附注", "Chinese legacy watching group migrates directly to Watching list without changing its content");
        string chineseMigrated = store.Load().Text; store.Initialize();
        Check(store.Load().Text == chineseMigrated, "Chinese migration is also idempotent on reopen");

        string both = "## To read\n- [ ] Old row\n- [ ]\n\n## Watching list\n- [ ] [](<https://example.com/new>)\n  > New note\n\n## 打算看\n- [x] Keep separately\n";
        store = MigrationStore(both); store.Initialize(); store.Initialize();
        Check(store.Load().Text == both && store.Load().Groups.Count == 3 && store.Load().Entries.Count == 4, "When Watching list already exists, preserve every old group and its rows without merging or loss");

        string twoLegacy = "## To read\n- [ ] Latest legacy\n\n## 打算看\n- [ ] Older legacy\n";
        store = MigrationStore(twoLegacy); store.Initialize();
        Check(store.Load().Text == twoLegacy.Replace("## To read\n", "## Watching list\n") && store.Load().Groups.Contains("打算看"), "With two old aliases, migrate To read once and preserve the separate Chinese group");

        string modern = "## Watching list\n- [ ]\n- [ ] [](<https://example.com/first>)\n  > A note before a title\n";
        store = MigrationStore(modern); store.Initialize(); store.Initialize();
        Check(store.Load().Text == modern && store.Load().Entries.Count == 2, "Existing Watching list rows remain byte-for-byte unchanged on initialization");
    }

    static void Main() {
        string source = "# Current\r\n\r\nKeep this introduction.\r\n\r\n## Work\r\n- [ ] First\r\n  > Keep the anchor note\r\n- [ ] Last\r\n\r\n## To read\r\n- [ ] Reading\r\n";
        var original = NoteDocument.Parse(source);
        Entry inserted;
        var replacement = Item("Middle"); replacement.Note = "New note";
        string next = original.InsertAfter(original.Entries[0], replacement, "Work", out inserted);
        var edited = NoteDocument.Parse(next);
        Check(Titles(edited) == "First|Middle|Last|Reading", "Insert directly after the selected item, before the next one");
        Check(next == source.Replace("  > Keep the anchor note\r\n", "  > Keep the anchor note\r\n- [ ] Middle\r\n  > New note\r\n"), "Preserve every unrelated line and CRLF while keeping the anchor note together");
        Check(inserted.Line == 7 && inserted.Span == 2 && inserted.Raw == "- [ ] Middle\r\n  > New note" && inserted.Group == "Work", "Return the persisted inserted entry with its note span and actual line");
        Check(original.Text == source && original.Entries[0].Raw.EndsWith("Keep the anchor note"), "Insertion does not mutate the source document or anchor");

        next = original.InsertBefore(original.Entries[0], Item("Before first"), "Work", out inserted);
        Check(Titles(NoteDocument.Parse(next)) == "Before first|First|Last|Reading" && inserted.Line == original.Entries[0].Line, "Insert a replacement at the cleared first row's slot without appending");
        Check(next.Contains("- [ ] First\r\n  > Keep the anchor note"), "Inserting before an item preserves its entire note span");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## Work\n- [ ] Last\n").InsertBefore(original.Entries[0], Item("New"), "Work", out inserted); }, "Refuse a stale before-anchor instead of moving the replacement elsewhere");
        Reject<ArgumentNullException>(delegate { original.InsertBefore(null, Item("New"), "Work", out inserted); }, "An absent before-anchor cannot silently turn into an append");

        string independentlyEdited = source.Replace("Keep this introduction.", "Keep this introduction.\r\nAn unrelated new paragraph.").Replace("- [ ] Last", "- [ ] Last edited elsewhere");
        var latest = NoteDocument.Parse(independentlyEdited);
        next = latest.InsertAfter(original.Entries[0], Item("Middle"), "Work");
        Check(next.Contains("An unrelated new paragraph.") && next.Contains("Last edited elsewhere") && Titles(NoteDocument.Parse(next)) == "First|Middle|Last edited elsewhere|Reading", "Resolve a stale line number against unchanged anchor content and preserve external edits");

        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(source.Replace("- [ ] First", "- [x] First")).InsertAfter(original.Entries[0], Item("New"), "Work"); }, "Refuse an anchor completed elsewhere");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse(source.Replace("Keep the anchor note", "Changed note")).InsertAfter(original.Entries[0], Item("New"), "Work"); }, "Refuse an anchor whose continuation note changed elsewhere");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## Work\n- [ ] Last\n").InsertAfter(original.Entries[0], Item("New"), "Work"); }, "Refuse a deleted anchor without appending to a different item");
        Reject<InvalidOperationException>(delegate { original.InsertAfter(original.Entries[0], Item("New"), "To read"); }, "Refuse an anchor from a different target topic");

        var duplicateDocument = NoteDocument.Parse("## Work\n- [ ] Same\n- [ ] Separator\n- [ ] Same\n- [ ] End\n");
        next = duplicateDocument.InsertAfter(duplicateDocument.Entries[2], Item("Inserted"), "Work");
        Check(Titles(NoteDocument.Parse(next)) == "Same|Separator|Same|Inserted|End", "Select the correct occurrence of an unchanged duplicate anchor");
        next = duplicateDocument.InsertBefore(duplicateDocument.Entries[2], Item("Before duplicate"), "Work", out inserted);
        Check(Titles(NoteDocument.Parse(next)) == "Same|Separator|Before duplicate|Same|End", "Insert before the correct duplicate occurrence");
        var fewerDuplicates = NoteDocument.Parse("## Work\n- [ ] Same\n- [ ] Separator\n");
        Reject<InvalidOperationException>(delegate { fewerDuplicates.InsertAfter(duplicateDocument.Entries[0], Item("New"), "Work"); }, "Refuse an ambiguous anchor when a duplicate disappears");
        var moreDuplicates = NoteDocument.Parse(duplicateDocument.Text + "- [ ] Same\n");
        Reject<InvalidOperationException>(delegate { moreDuplicates.InsertAfter(duplicateDocument.Entries[2], Item("New"), "Work"); }, "Refuse an ambiguous anchor when another duplicate appears");
        next = duplicateDocument.InsertAfter(duplicateDocument.Entries[2], Item("Same"), "Work", out inserted);
        Check(inserted.Duplicates == 3 && inserted.Occurrence == 2 && inserted.Line == 4, "Return correct duplicate metadata when the inserted title repeats");
        next = NoteDocument.Parse(next).InsertAfter(inserted, Item("Following"), "Work");
        Check(Titles(NoteDocument.Parse(next)) == "Same|Separator|Same|Same|Following|End", "Use the returned duplicate entry as the next inline insertion anchor");
        var differentNotes = NoteDocument.Parse("## Work\n- [ ] Same\n  > First note\n- [ ] Same\n  > Second note\n");
        next = differentNotes.InsertAfter(differentNotes.Entries[1], Item("Following"), "Work");
        Check(next.EndsWith("  > Second note\n- [ ] Following\n"), "Different continuation notes identify otherwise identical titles");

        string fenced = "# Current\n```md\n## Work\n- [ ] A code example\n```\n\n## Work  \nParagraph.\n~~~md\n## Fake\n# Still code\n- [ ] Another code example\n~~~\n\n\n## To read\nKeep this final prose.\n";
        next = NoteDocument.Parse(fenced).InsertAfter(null, Item("Appended"), "Work", out inserted);
        Check(next == fenced.Replace("~~~\n\n\n## To read", "~~~\n- [ ] Appended\n\n\n## To read"), "Append before the real section boundary while preserving fenced fake headings, prose, and spacing");
        Check(inserted.Group == "Work" && inserted.Title == "Appended" && NoteDocument.Parse(next).Entries.Count == 1, "Read the appended row as a real task outside the code fence");
        var emptyTopic = NoteDocument.Parse("## Work\n\n## To read\n");
        Check(emptyTopic.InsertAfter(null, Item("First"), "Work") == "## Work\n- [ ] First\n\n## To read\n", "Append to an existing empty topic without adding a heading");
        var finalTopic = NoteDocument.Parse("## Work\n- [ ] First");
        Check(finalTopic.InsertAfter(null, Item("Last"), "Work") == "## Work\n- [ ] First\n- [ ] Last", "Append to a final topic without changing its missing terminal newline");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## To read\n").InsertAfter(null, Item("New"), "Work"); }, "Refuse to recreate a topic deleted before an append");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("```md\n## Work\n```\n").InsertAfter(null, Item("New"), "Work"); }, "Do not mistake a fenced heading for an existing topic");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## Work\n```md\nUnclosed\n").InsertAfter(null, Item("New"), "Work"); }, "Refuse to save an appended item inside an unclosed code fence");
        Reject<InvalidOperationException>(delegate { NoteDocument.Parse("## To read\n").InsertAfter(original.Entries[0], Item("New"), "Work"); }, "Refuse insertion when the anchor's entire topic was deleted");

        Reject<ArgumentNullException>(delegate { original.InsertAfter(null, null, "Work"); }, "Reject null replacement entries");
        Check(NoteDocument.Parse(original.InsertAfter(null, Item("  "), "Work")).Entries.Count == original.Entries.Count + 1, "Allow an intentional blank inline row");
        Reject<ArgumentException>(delegate { original.InsertAfter(null, Item("One\n## Injected"), "Work"); }, "Reject multiline inline titles");
        Reject<ArgumentException>(delegate { var unsafeItem = Item("Unsafe"); unsafeItem.Link = "javascript:alert(1)"; original.InsertAfter(null, unsafeItem, "Work"); }, "Validate links before inline insertion");
        var linkedItem = Item("Read [one] \\ two"); linkedItem.Link = "https://example.com/a(b)?x=1&y=2";
        next = original.InsertAfter(original.Entries[0], linkedItem, "Work", out inserted);
        Check(inserted.Title == linkedItem.Title && inserted.Link == linkedItem.Link, "Round-trip inline titles and links through the existing Markdown serializer");

        string scratch = Path.Combine(Path.GetTempPath(), "shike-inline-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var store = new NoteStore(scratch, Path.Combine(scratch, "backups"));
        File.WriteAllText(store.CurrentPath, source);
        var staleAnchor = store.Load().Entries[0];
        File.WriteAllText(store.CurrentPath, independentlyEdited);
        string before = store.InsertAfter(staleAnchor, Item("Stored"), "Work", out inserted);
        Check(before == independentlyEdited && store.Load().Text.Contains("An unrelated new paragraph."), "Store returns the latest pre-edit text and keeps independent file edits");
        Check(store.Load().Entries.Single(x => x.Line == inserted.Line).Raw == inserted.Raw && inserted.Title == "Stored", "Store returns the inserted entry only after a successful durable save");
        Check(File.ReadAllText(Path.Combine(store.BackupDirectory, "当前.last-good.md")) == independentlyEdited, "Back up the latest external edits before storing the inline item");
        string saved = store.Load().Text;
        store.Save(saved, before);
        Check(store.Load().Text == independentlyEdited, "Returned previous text supports exact undo without losing external edits");
        File.WriteAllText(store.CurrentPath, "## To read\n");
        Reject<InvalidOperationException>(delegate { store.InsertAfter(null, Item("New"), "Work"); }, "Store refuses to resurrect a topic deleted externally");
        Check(File.ReadAllText(store.CurrentPath) == "## To read\n", "A refused store insertion leaves the file untouched");
        File.WriteAllText(store.CurrentPath, source);
        Reject<ArgumentException>(delegate { store.InsertAfter(null, Item("Invalid\nTitle"), "Work"); }, "Store rejects invalid inline input before writing");
        Check(File.ReadAllText(store.CurrentPath) == source && Directory.GetFiles(scratch, "*.tmp").Length == 0, "Validation failures leave no temporary note files or content changes");
        BlankRows(); EmptyTopics(); WatchingListMigration();
        Console.WriteLine("Passed " + checks + " inline data checks. Test vault: " + scratch);
    }
}
