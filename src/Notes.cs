using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Shike {
    public sealed class Entry {
        public string Group, Title, Link, Note, Raw;
        public bool Done;
        public int Line, Span, Occurrence, Duplicates;
    }
    public sealed class NoteDocument {
        public string Text, Newline;
        public string[] Lines;
        public List<string> Groups = new List<string>();
        public List<Entry> Entries = new List<Entry>();
        static readonly Regex Task = new Regex(@"^[-*] \[(?<done>[ xX])\](?:[ \t]+(?<body>.*))?$");
        static readonly Regex EmptyGroupToken = new Regex(@"^<!-- shike-topic:[0-9a-fA-F]{32} -->$");
        static readonly Regex Link = new Regex(@"^\[(?<name>(?:\\.|[^\]])*)\]\((?:<(?<angle>[^>]+)>|(?<url>.+))\)$");
        static readonly Regex Wiki = new Regex(@"^\[\[(?<path>[^\]|]+)(?:\|(?<name>[^\]]+))?\]\]$");

        public static NoteDocument Parse(string text) {
            var d = new NoteDocument { Text = text, Newline = text.Contains("\r\n") ? "\r\n" : "\n", Lines = Regex.Split(text, "\r?\n") };
            string group = null, fence = null;
            for (int i = 0; i < d.Lines.Length; i++) {
                string line = d.Lines[i], trimmed = line.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) {
                    string marker = trimmed.Substring(0, 3);
                    if (fence == null) fence = marker; else if (fence == marker) fence = null;
                    continue;
                }
                if (fence != null) continue;
                if (line.StartsWith("## ")) { group = line.Substring(3).Trim(); if (!d.Groups.Contains(group)) d.Groups.Add(group); continue; }
                if (line.StartsWith("# ")) { group = null; continue; }
                var m = Task.Match(line);
                if (!m.Success || group == null) continue;
                var e = new Entry { Group = group, Title = m.Groups["body"].Value, Link = "", Note = "", Done = m.Groups["done"].Value != " ", Line = i, Span = 1, Raw = line };
                var lm = Link.Match(e.Title); var wm = Wiki.Match(e.Title);
                if (lm.Success) { e.Title = Unescape(lm.Groups["name"].Value); e.Link = lm.Groups["angle"].Success ? lm.Groups["angle"].Value : lm.Groups["url"].Value; }
                else if (wm.Success) { e.Link = wm.Groups["path"].Value; e.Title = wm.Groups["name"].Success ? wm.Groups["name"].Value : Path.GetFileNameWithoutExtension(e.Link); }
                else e.Title = Unescape(e.Title);
                if (i + 1 < d.Lines.Length && d.Lines[i + 1].StartsWith("  > ")) { e.Note = d.Lines[i + 1].Substring(4); e.Raw += d.Newline + d.Lines[++i]; e.Span = 2; }
                d.Entries.Add(e);
            }
            foreach (var e in d.Entries) { var same = d.Entries.Where(x => x.Group == e.Group && x.Raw == e.Raw).ToList(); e.Duplicates = same.Count; e.Occurrence = same.IndexOf(e); }
            return d;
        }
        static string Unescape(string s) { return Regex.Replace(s, @"\\([\\\[\]])", "$1"); }
        static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]"); }
        public static string NewEmptyGroup() { return "<!-- shike-topic:" + Guid.NewGuid().ToString("N") + " -->"; }
        public static bool IsEmptyGroup(string group) { return group != null && EmptyGroupToken.IsMatch(group); }
        public static string GroupTitle(string group) { return IsEmptyGroup(group) ? "" : group; }
        public static string Serialize(Entry e, string nl) {
            NoteStore.Validate(e);
            string title = Escape((e.Title ?? "").Trim());
            if (!string.IsNullOrWhiteSpace(e.Link)) title = "[" + title + "](<" + e.Link.Trim().Replace("<", "%3C").Replace(">", "%3E") + ">)";
            return "- [" + (e.Done ? "x" : " ") + "]" + (title.Length == 0 ? "" : " " + title) + (string.IsNullOrWhiteSpace(e.Note) ? "" : nl + "  > " + e.Note.Trim());
        }
        public string InsertAfter(Entry anchor, Entry replacement, string group) {
            Entry inserted;
            return InsertAfter(anchor, replacement, group, out inserted);
        }
        public string InsertAfter(Entry anchor, Entry replacement, string group, out Entry inserted) {
            return InsertRelative(anchor, replacement, group, false, out inserted);
        }
        public string InsertBefore(Entry anchor, Entry replacement, string group, out Entry inserted) {
            if (anchor == null) throw new ArgumentNullException("anchor");
            return InsertRelative(anchor, replacement, group, true, out inserted);
        }
        string InsertRelative(Entry anchor, Entry replacement, string group, bool before, out Entry inserted) {
            inserted = null;
            if (replacement == null) throw new ArgumentNullException("replacement");
            NoteStore.Validate(replacement);
            if (!Groups.Contains(group)) throw new InvalidOperationException("This topic changed elsewhere. The list has refreshed; please try again.");
            int insertion;
            if (anchor != null) {
                var matches = Entries.Where(x => x.Group == anchor.Group && x.Raw == anchor.Raw).ToList();
                if (anchor.Group != group || anchor.Occurrence < 0 || matches.Count != anchor.Duplicates || anchor.Occurrence >= matches.Count)
                    throw new InvalidOperationException("This item changed elsewhere. The list has refreshed; please try again.");
                var found = matches[anchor.Occurrence];
                insertion = found.Line + (before ? 0 : found.Span);
            } else {
                insertion = GroupAppendLine(group);
            }
            var lines = Lines.ToList();
            lines.InsertRange(insertion, Regex.Split(Serialize(replacement, Newline), "\r?\n"));
            string next = string.Join(Newline, lines);
            // Return the actual occurrence after parsing, so a newly inserted
            // duplicate can safely become the anchor for the next inline row.
            inserted = Parse(next).Entries.FirstOrDefault(x => x.Line == insertion && x.Group == group);
            if (inserted == null) throw new InvalidOperationException("This topic's formatting changed. Refresh the list before adding an item.");
            return next;
        }
        int GroupAppendLine(string group) {
            int heading = -1, end = Lines.Length;
            string fence = null;
            for (int i = 0; i < Lines.Length; i++) {
                string line = Lines[i], trimmed = line.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) {
                    string marker = trimmed.Substring(0, 3);
                    if (fence == null) fence = marker; else if (fence == marker) fence = null;
                    continue;
                }
                if (fence != null) continue;
                if (!line.StartsWith("## ") && !line.StartsWith("# ")) continue;
                if (heading >= 0) { end = i; break; }
                if (line.StartsWith("## ") && line.Substring(3).Trim() == group) heading = i;
            }
            if (heading < 0) throw new InvalidOperationException("This topic changed elsewhere. The list has refreshed; please try again.");
            if (fence != null) throw new InvalidOperationException("Close the code block in this topic before adding an item.");
            while (end > heading + 1 && Lines[end - 1] == "") end--;
            return end;
        }
        public string Change(Entry original, Entry replacement, string destination) {
            var lines = Lines.ToList();
            if (original != null) {
                var matches = Entries.Where(x => x.Group == original.Group && x.Raw == original.Raw).ToList();
                if (matches.Count != original.Duplicates || original.Occurrence >= matches.Count)
                    throw new InvalidOperationException("This item changed elsewhere. The list has refreshed; please try again.");
                var found = matches[original.Occurrence];
                lines.RemoveRange(found.Line, found.Span);
                if (replacement != null && destination == original.Group) {
                    lines.InsertRange(found.Line, Regex.Split(Serialize(replacement, Newline), "\r?\n"));
                    return string.Join(Newline, lines);
                }
            }
            if (replacement == null) return string.Join(Newline, lines);
            int heading = lines.FindIndex(x => x == "## " + destination);
            if (heading < 0) { if (lines.Count > 0 && lines[lines.Count - 1] != "") lines.Add(""); lines.Add("## " + destination); lines.Add(""); heading = lines.Count - 2; }
            int end = heading + 1;
            while (end < lines.Count && !Regex.IsMatch(lines[end], @"^#{1,2} ")) end++;
            while (end > heading + 1 && lines[end - 1] == "") end--;
            lines.InsertRange(end, Regex.Split(Serialize(replacement, Newline), "\r?\n"));
            return string.Join(Newline, lines);
        }
        public string Move(Entry original, Entry before) {
            if (original == null) throw new ArgumentNullException("original");
            var source = ResolveMoveEntry(original);
            if (before != null && before.Group != source.Group)
                throw new InvalidOperationException("Move items within the same topic.");
            // Resolve both occurrences before removing anything: removing one
            // duplicate changes the occurrence numbers of the remaining rows.
            var anchor = before == null ? null : ResolveMoveEntry(before);
            var last = Entries.Last(x => x.Group == source.Group);
            int insertion = anchor == null ? last.Line + last.Span : anchor.Line;
            if (insertion == source.Line || insertion == source.Line + source.Span) return Text;
            var lines = Lines.ToList();
            var block = lines.GetRange(source.Line, source.Span);
            lines.RemoveRange(source.Line, source.Span);
            if (insertion > source.Line) insertion -= source.Span;
            // Preserve external Markdown formatting and the attached note block.
            lines.InsertRange(insertion, block);
            return string.Join(Newline, lines);
        }
        Entry ResolveMoveEntry(Entry original) {
            var matches = Entries.Where(x => x.Group == original.Group && x.Raw == original.Raw).ToList();
            if (original.Occurrence < 0 || matches.Count != original.Duplicates || original.Occurrence >= matches.Count)
                throw new InvalidOperationException("This item changed elsewhere. The list has refreshed; please try again.");
            return matches[original.Occurrence];
        }
        List<Tuple<int, int>> GroupSpans(string group) {
            var spans = new List<Tuple<int, int>>();
            int start = -1; string fence = null;
            for (int line = 0; line < Lines.Length; line++) {
                string text = Lines[line], trimmed = text.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) {
                    string marker = trimmed.Substring(0, 3);
                    if (fence == null) fence = marker; else if (fence == marker) fence = null;
                    continue;
                }
                if (fence != null || !(text.StartsWith("# ") || text.StartsWith("## "))) continue;
                if (start >= 0) { spans.Add(Tuple.Create(start, line - start)); start = -1; }
                if (text.StartsWith("## ") && text.Substring(3).Trim() == group) start = line;
            }
            if (start >= 0) spans.Add(Tuple.Create(start, Lines.Length - start));
            return spans;
        }
        public string[] GroupSections(string group) {
            return GroupSpans(group).Select(span => string.Join(Newline, Lines.Skip(span.Item1).Take(span.Item2))).ToArray();
        }
        public string ChangeGroup(string original, string name) {
            if (name != null) {
                name = name.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Contains("\n") || name.Contains("\r") || (!IsEmptyGroup(name) && name.Length > 60)) throw new ArgumentException("Enter a short topic name.");
                if (name != original && Groups.Contains(name)) throw new ArgumentException("A topic with this name already exists.");
            }
            var lines = Lines.ToList();
            if (original == null) {
                if (lines.Count > 0 && lines[lines.Count - 1] != "") lines.Add("");
                lines.Add("## " + name); lines.Add("");
            } else {
                var spans = GroupSpans(original);
                if (spans.Count == 0) throw new InvalidOperationException("This topic changed elsewhere. Refresh and try again.");
                // Parse merges repeated headings into one column. Mutate all
                // real sections in reverse order, leaving fenced examples alone.
                for (int index = spans.Count - 1; index >= 0; index--) {
                    var span = spans[index];
                    if (name != null) lines[span.Item1] = "## " + name;
                    else lines.RemoveRange(span.Item1, span.Item2);
                }
            }
            return string.Join(Newline, lines);
        }
    }
    public sealed class NoteStore {
        public const string ReadingGroup = "Watching list";
        public string Vault, CurrentPath, BackupDirectory;
        public NoteStore(string vault, string backups) {
            Vault = Path.GetFullPath(vault); CurrentPath = Path.Combine(Vault, "当前.md");
            BackupDirectory = backups;
        }
        public void Initialize() {
            Directory.CreateDirectory(Vault);
            if (!File.Exists(CurrentPath)) CreateOnly(CurrentPath, "# 当前\n\n");
            var current = Load();
            if (!current.Groups.Contains(ReadingGroup)) {
                string previous = current.Groups.Contains("To read") ? "To read" : current.Groups.Contains("打算看") ? "打算看" : null;
                if (previous != null) {
                    // Rename only real headings, including repeated sections.
                    // Keep their spacing, all rows, and fenced examples intact.
                    var lines = (string[])current.Lines.Clone(); string fence = null;
                    for (int i = 0; i < lines.Length; i++) {
                        string trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~")) {
                            string marker = trimmed.Substring(0, 3);
                            if (fence == null) fence = marker; else if (fence == marker) fence = null;
                            continue;
                        }
                        if (fence != null || !lines[i].StartsWith("## ") || lines[i].Substring(3).Trim() != previous) continue;
                        int start = lines[i].IndexOf(previous, 3, StringComparison.Ordinal);
                        lines[i] = lines[i].Substring(0, start) + ReadingGroup + lines[i].Substring(start + previous.Length);
                    }
                    string updated = string.Join(current.Newline, lines);
                    Save(current.Text, updated); current = NoteDocument.Parse(updated);
                }
            }
            if (!current.Groups.Contains(ReadingGroup)) Save(current.Text, current.ChangeGroup(null, ReadingGroup));
        }
        static void CreateOnly(string path, string text) {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
        }
        public NoteDocument Load() { return NoteDocument.Parse(File.ReadAllText(CurrentPath, Encoding.UTF8)); }
        public string Mutate(Entry original, Entry replacement, string group) {
            var latest = Load(); string next = latest.Change(original, replacement, group); Save(latest.Text, next); return latest.Text;
        }
        public string Move(Entry original, Entry before, out NoteDocument moved) {
            moved = null;
            var latest = Load();
            string next = latest.Move(original, before);
            var result = NoteDocument.Parse(next);
            if (next != latest.Text) Save(latest.Text, next);
            moved = result;
            return latest.Text;
        }
        public string InsertAfter(Entry anchor, Entry replacement, string group) {
            Entry inserted;
            return InsertAfter(anchor, replacement, group, out inserted);
        }
        public string InsertAfter(Entry anchor, Entry replacement, string group, out Entry inserted) {
            inserted = null;
            var latest = Load();
            Entry candidate;
            string next = latest.InsertAfter(anchor, replacement, group, out candidate);
            Save(latest.Text, next);
            inserted = candidate;
            return latest.Text;
        }
        public void Save(string expected, string next) {
            string tmp = CurrentPath + ".shike-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                // Durable temporary file; replacement is atomic on the vault's volume.
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(next); fs.Write(bytes, 0, bytes.Length); fs.Flush(true);
                }
                if (File.ReadAllText(CurrentPath, Encoding.UTF8) != expected) throw new InvalidOperationException("Your note changed in another app. Refresh and try again.");
                Directory.CreateDirectory(BackupDirectory);
                File.WriteAllText(Path.Combine(BackupDirectory, "当前.last-good.md"), expected, new UTF8Encoding(false));
                File.Replace(tmp, CurrentPath, null);
            } finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        public static void Validate(Entry e) {
            if (e == null) throw new ArgumentNullException("e");
            if (new[] { e.Title ?? "", e.Link ?? "", e.Note ?? "", e.Group ?? "" }.Any(s => s.Contains("\n") || s.Contains("\r"))) throw new ArgumentException("Keep each field on a single line.");
            if ((e.Title ?? "").Length > 400 || (e.Note ?? "").Length > 1000) throw new ArgumentException("Shorten the name or note before saving.");
            if (!string.IsNullOrWhiteSpace(e.Link)) Links.Validate(e.Link);
        }
    }
    public static class Links {
        public static void Validate(string link) {
            if (link.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new ArgumentException("This link contains invalid characters.");
            Uri uri;
            if (Uri.TryCreate(link, UriKind.Absolute, out uri) && !uri.IsFile) {
                if (uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "obsidian") throw new ArgumentException("Use a web link, Obsidian link, or local note path.");
            }
        }
        public static string Resolve(string link, string vault) {
            Validate(link); Uri uri;
            if (Uri.TryCreate(link, UriKind.Absolute, out uri) && !uri.IsFile) return link;
            int hash = link.IndexOf('#');
            if (hash >= 0) link = link.Substring(0, hash);
            string decoded = Uri.UnescapeDataString(link);
            string path = Uri.TryCreate(decoded, UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : Path.GetFullPath(Path.Combine(vault, decoded.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(path) && !Path.HasExtension(path)) path += ".md";
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("File not found. Edit the item to update its link.", path);
            if (Directory.Exists(path)) return path;
            string[] allowed = { ".pdf", ".txt", ".md", ".png", ".jpg", ".jpeg", ".webp", ".epub", ".docx", ".pptx", ".xlsx" };
            if (!allowed.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("Use a note, document, or folder link. Open programs and scripts in File Explorer.");
            return path;
        }
    }
}
