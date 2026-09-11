using System;
using System.IO;
using System.Linq;
using Shike;

class Tests {
    static int count;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
    static void Main() {
        string scratch = Path.Combine(Path.GetTempPath(), "shike-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        var store = new NoteStore(scratch, Path.Combine(scratch, "backups"));
        store.Initialize(); var initial = store.Load();
        Check(initial.Groups.SequenceEqual(new[] { "Watching list" }) && initial.Entries.Count == 0, "Only the requested empty Watching list column is initialized");
        Check(!Directory.Exists(Path.Combine(scratch, "00-Inbox")), "Do not create capture notes or folders");
        string withGroup = initial.ChangeGroup(null, "我的主题");
        var gd = NoteDocument.Parse(withGroup);
        Check(gd.Groups.Contains("我的主题") && gd.Entries.Count == 0, "Create an empty topic without assumed tasks");
        Reject(delegate { gd.ChangeGroup(null, "我的主题"); }, "Reject duplicate topics");
        gd = NoteDocument.Parse(gd.ChangeGroup("我的主题", "新的名字"));
        Check(gd.Groups.Contains("新的名字"), "Rename a topic");
        Check(NoteDocument.Parse(gd.ChangeGroup("新的名字", null)).Groups.SequenceEqual(new[] { NoteStore.ReadingGroup }), "Removing a topic preserves the reading column");
        File.AppendAllText(store.CurrentPath, "Keep my original note\n"); store.Initialize();
        Check(File.ReadAllText(store.CurrentPath).Contains("Keep my original note") && store.Load().Groups.Count == 1, "Initialization preserves existing content and does not duplicate reading column");
        File.WriteAllText(store.CurrentPath, "# Current\n\n## 打算看\n- [ ] Keep this link\n"); store.Initialize();
        Check(store.Load().Groups.SequenceEqual(new[] { NoteStore.ReadingGroup }) && store.Load().Entries.Single().Title == "Keep this link", "Chinese reading-column migration targets Watching list and preserves existing items");
        string source = "# 当前\r\n\r\nAn untouched paragraph.\r\n\r\n## 测试主题\r\n- [ ] [条目 [A]](<https://example.com/a(b)?x=1&y=2>)\r\n  > 附加说明\r\n\r\n## 其他主题\r\n```md\r\n- [ ] not a task\r\n```\r\n";
        File.WriteAllText(store.CurrentPath, source); var d = store.Load();
        // A literal nested bracket is not a Markdown link. App serialization always escapes brackets.
        var newItem = new Entry { Title = "条目 [A] \\ 示例", Link = "https://example.com/a(b)?x=1&y=2", Note = "附加说明", Group = "测试主题" };
        store.Mutate(d.Entries[0], newItem, "测试主题"); d = store.Load();
        Check(d.Entries.Count == 1 && d.Entries[0].Title == newItem.Title && d.Entries[0].Link == newItem.Link, "Round-trip punctuation, brackets, backslashes, parentheses and URL query strings");
        Check(d.Text.Contains("An untouched paragraph.\r\n") && d.Text.Contains("```md\r\n- [ ] not a task\r\n```"), "Preserve prose, fenced code, and CRLF line endings");
        var stale = d.Entries[0]; File.AppendAllText(store.CurrentPath, "\r\nOutside edit\r\n"); newItem.Done = true; store.Mutate(stale, newItem, "测试主题"); d = store.Load();
        Check(d.Entries[0].Done && d.Text.Contains("Outside edit"), "Completion keeps independent Obsidian edits");
        Reject(delegate { store.Mutate(stale, newItem, "测试主题"); }, "Reject a stale edit of the same item");
        var beforeMove = d.Entries[0]; store.Mutate(beforeMove, newItem, "其他主题"); d = store.Load();
        Check(d.Entries[0].Group == "其他主题" && d.Entries[0].Note == newItem.Note, "Move task and continuation hint together");
        var beforeRemove = d; store.Mutate(d.Entries[0], null, "其他主题"); var removed = store.Load();
        Check(removed.Entries.Count == 0, "Remove a task without leaving its continuation hint");
        store.Save(removed.Text, beforeRemove.Text); Check(store.Load().Entries.Count == 1, "Undo restores previous document");
        Reject(delegate { store.Save("stale document", source); }, "Undo cannot overwrite later external edits");
        var wiki = NoteDocument.Parse("## 测试主题\n- [ ] [[Examples/入口|示例入口]]\n");
        Check(wiki.Entries[0].Link == "Examples/入口" && wiki.Entries[0].Title == "示例入口", "Read Obsidian wikilinks");
        var duplicates = NoteDocument.Parse("## 测试主题\n- [ ] 同名\n- [ ] 同名\n");
        string editedDuplicate = duplicates.Change(duplicates.Entries[1], new Entry { Title = "第二条", Link = "", Note = "" }, "测试主题");
        Check(editedDuplicate.Contains("- [ ] 同名\n- [ ] 第二条"), "Target the correct occurrence of duplicate task names");
        var changedDuplicates = NoteDocument.Parse("## 测试主题\n- [ ] 同名\n");
        Reject(delegate { changedDuplicates.Change(duplicates.Entries[0], null, "测试主题"); }, "Reject ambiguous edits after a duplicate disappears");
        Reject(delegate { Links.Validate("javascript:alert(1)"); }, "Reject executable URL schemes");
        NoteStore.Validate(new Entry { Title = " ", Link = "", Note = "" });
        Check(NoteDocument.Serialize(new Entry { Title = " ", Link = "", Note = "" }, "\n") == "- [ ]", "Persist intentional blank rows without assumed names");
        var linkFirst = new Entry { Title = "", Link = "https://example.com", Note = "Read the abstract", Group = NoteStore.ReadingGroup };
        NoteStore.Validate(linkFirst);
        var linkFirstReloaded = NoteDocument.Parse("## " + NoteStore.ReadingGroup + "\n" + NoteDocument.Serialize(linkFirst, "\n")).Entries.Single();
        Check(linkFirstReloaded.Title == "" && linkFirstReloaded.Link == linkFirst.Link && linkFirstReloaded.Note == linkFirst.Note, "A Watching list URL and note can be saved before its name");
        string localNote = Path.Combine(scratch, "测试 笔记.md");
        File.WriteAllText(localNote, "# Test");
        Check(Links.Resolve("测试 笔记.md#开头", scratch) == localNote, "Resolve a local Markdown link with Chinese text, spaces and a heading fragment to the file itself");
        Check(Links.Resolve("测试 笔记", scratch) == localNote, "Resolve an extensionless local note without requiring Obsidian");
        Check(Links.Resolve(new Uri(localNote).AbsoluteUri + "#heading", scratch) == localNote, "Resolve a file URI with escaped Chinese text, spaces and a fragment to the local file");
        string explicitObsidian = "obsidian://open?vault=Example&file=Notes%2FExample%23Heading";
        Check(Links.Resolve(explicitObsidian, scratch) == explicitObsidian, "Preserve an explicitly supplied Obsidian URI without rewriting it");
        string webLink = "https://example.com/notes.md#heading";
        Check(Links.Resolve(webLink, scratch) == webLink, "Preserve web Markdown URLs and their fragments");
        File.WriteAllText(Path.Combine(scratch, "bad.cmd"), "echo test");
        Reject(delegate { Links.Resolve("bad.cmd", scratch); }, "Do not launch scripts from task links");
        Check(!Directory.GetFiles(scratch, "*.tmp").Any(), "Leave no temporary document files after success or failure");
        Console.WriteLine("Passed " + count + " checks. Test vault: " + scratch);
    }
}
