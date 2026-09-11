using System;
using System.IO;
using System.Linq;
using Shike;

static class PortableFirstRunTests {
    static int checks;
    static void Check(bool value, string message) {
        if (!value) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    static NoteStore OpenStore(Settings settings, string directory) {
        var store = new NoteStore(settings.Vault, Path.Combine(directory, "backups"));
        store.Initialize(); return store;
    }
    static void Run(string output) {
        string first = Path.Combine(output, "first-copy");
        var settings = Settings.LoadForLaunch(first, null);
        Check(settings.PortableNotes && settings.Vault == Path.Combine(first, "notes"), "A fresh portable copy uses only its own notes directory");
        Check(!Directory.Exists(first), "Resolving a fresh launch does not write notes or settings");
        Check(settings.Left == -1 && settings.Top == -1 && settings.Width == 860 && settings.Height == 330 && settings.InterfaceScale == 1 && settings.LaunchLayout == null,
            "A fresh launch contains no personal window preset");
        var firstStore = OpenStore(settings, first);
        Check(firstStore.Load().Groups.SequenceEqual(new[] { NoteStore.ReadingGroup }) && firstStore.Load().Entries.Count == 0,
            "The initial document contains only an empty Watching list");
        settings.Save(first);
        Check(Settings.LoadForLaunch(first, null).PortableNotes && Settings.LoadForLaunch(first, null).Vault == settings.Vault, "The portable notes preference survives restart");

        firstStore.Mutate(null, new Entry { Title = "Private fixture", Link = "", Note = "" }, NoteStore.ReadingGroup);
        string firstNotes = File.ReadAllText(firstStore.CurrentPath);
        string second = Path.Combine(output, "second-copy");
        var other = Settings.LoadForLaunch(second, null);
        var otherStore = OpenStore(other, second);
        Check(other.Vault == Path.Combine(second, "notes") && otherStore.Load().Entries.Count == 0,
            "Another fresh copy never adopts the first copy's list");
        Check(File.ReadAllText(firstStore.CurrentPath) == firstNotes, "Initializing another copy leaves existing notes untouched");

        string moved = Path.Combine(output, "moved-copy");
        string movedNotes = Path.Combine(moved, "notes");
        Directory.CreateDirectory(movedNotes);
        File.Copy(Path.Combine(first, "settings.json"), Path.Combine(moved, "settings.json"));
        File.Copy(firstStore.CurrentPath, Path.Combine(movedNotes, Path.GetFileName(firstStore.CurrentPath)));
        firstStore.Mutate(null, new Entry { Title = "Only in original copy", Link = "", Note = "" }, NoteStore.ReadingGroup);
        string originalAfterCopy = File.ReadAllText(firstStore.CurrentPath);
        var movedSettings = Settings.LoadForLaunch(moved, null);
        var movedStore = OpenStore(movedSettings, moved);
        Check(movedSettings.PortableNotes && movedSettings.Vault == movedNotes && movedStore.CurrentPath != firstStore.CurrentPath,
            "Copied UserData resolves notes within the new location even while the original exists");
        Check(movedStore.Load().Entries.Count == 1 && movedStore.Load().Entries[0].Title == "Private fixture",
            "The relocated copy reads its carried notes rather than the original copy's later edits");
        movedStore.Mutate(null, new Entry { Title = "Only in relocated copy", Link = "", Note = "" }, NoteStore.ReadingGroup);
        Check(File.ReadAllText(firstStore.CurrentPath) == originalAfterCopy,
            "Editing relocated notes leaves the original copy untouched");
        movedSettings.Save(moved);
        Check(Settings.LoadForLaunch(moved, null).Vault == movedNotes,
            "The relocated notes location remains correct after saving and reopening");

        string existingState = Path.Combine(output, "existing-state");
        string chosenNotes = Path.Combine(output, "chosen-notes");
        var existing = new Settings { Vault = chosenNotes, DesignVersion = 2, WatchingTitle = "My list", Left = 23, Top = 57, Width = 940, Height = 420,
            InterfaceScale = .9, Transparency = 72, LaunchLayout = new PanelLayout { Left = 23, Top = 57, Width = 940, Height = 420, InterfaceScale = .9 } };
        existing.Save(existingState);
        var restored = Settings.LoadForLaunch(existingState, null);
        Check(!restored.PortableNotes && restored.Vault == chosenNotes && restored.WatchingTitle == "My list" && restored.Transparency == 72 && restored.LaunchLayout.Left == 23 && restored.LaunchLayout.InterfaceScale == .9,
            "Existing settings retain their explicitly saved notes and preferences");
        var chosenStore = OpenStore(restored, existingState);
        chosenStore.Mutate(null, new Entry { Title = "Existing fixture", Link = "", Note = "" }, NoteStore.ReadingGroup);
        string savedNotes = File.ReadAllText(chosenStore.CurrentPath);
        OpenStore(Settings.LoadForLaunch(existingState, null), existingState);
        Check(File.ReadAllText(chosenStore.CurrentPath) == savedNotes, "Opening an existing configured folder preserves its entries");

        string overrideNotes = Path.Combine(output, "command-line-notes");
        string savedSettings = File.ReadAllText(Path.Combine(existingState, "settings.json"));
        var overridden = Settings.LoadForLaunch(existingState, overrideNotes);
        Check(!overridden.PortableNotes && overridden.Vault == overrideNotes && overridden.WatchingTitle == "My list", "An explicit --vault overrides the stored notes path while keeping preferences");
        Check(File.ReadAllText(Path.Combine(existingState, "settings.json")) == savedSettings && File.ReadAllText(chosenStore.CurrentPath) == savedNotes,
            "Resolving an override does not alter the previous configuration or notes");
        Check(Settings.LoadForLaunch(Path.Combine(output, "fresh-override"), overrideNotes).Vault == overrideNotes,
            "An explicit --vault also works on a fresh launch");
        var portableOverride = Settings.LoadForLaunch(moved, overrideNotes);
        Check(!portableOverride.PortableNotes && portableOverride.Vault == overrideNotes,
            "An explicit --vault disables portable path rebasing for that configuration");
        portableOverride.Save(moved);
        var reopenedOverride = Settings.LoadForLaunch(moved, null);
        Check(!reopenedOverride.PortableNotes && reopenedOverride.Vault == overrideNotes,
            "A saved explicit override survives reopening instead of reverting to portable notes");
    }
    static int Main(string[] args) {
        try { Run(Path.GetFullPath(args[0])); Console.WriteLine(checks + " portable first-run checks passed without UI."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
