using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Shike;

static class SettingsPersistenceTests {
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Reject<T>(Action action, string label) where T : Exception {
        bool rejected = false; try { action(); } catch (T) { rejected = true; }
        Check(rejected, label);
    }
    static Settings Fixture() {
        return new Settings { DesignVersion = 2, Vault = Path.Combine(Path.GetTempPath(), "todo-example-notes"), WatchingTitle = "For later", WatchingWidth = 237.12345678901234,
            InterfaceScale = .91234567890123456, Left = 24.25, Top = 212.75, Width = 802.5, Height = 364.25, Zoom = 1.2, Transparency = 95 };
    }
    static void Run(string output) {
        Check(Settings.DefaultDirectory == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData"), "The default settings directory is the shared UserData folder beside the executable");
        string missing = Path.Combine(output, "missing");
        var initial = Settings.Load(missing);
        Check(initial.Width == 860 && initial.InterfaceScale == 1 && !Directory.Exists(missing), "A genuinely absent settings directory returns initial defaults without creating files");
        Directory.CreateDirectory(missing);
        Check(Settings.Load(missing).Width == 860, "A genuinely absent settings file returns initial defaults");

        string state = Path.Combine(output, "state"); var expected = Fixture(); expected.ApplyLaunchLayout(); expected.Save(state);
        var saved = Settings.Load(state);
        Check(saved.Left == expected.Left && saved.Top == expected.Top && saved.Width == expected.Width && saved.Height == expected.Height && saved.InterfaceScale == expected.InterfaceScale && saved.WatchingWidth == expected.WatchingWidth, "Fractional geometry, scale and column width round-trip exactly");
        Check(saved.LaunchLayout != null && saved.LaunchLayout.InterfaceScale == expected.InterfaceScale && saved.LaunchLayout.Top == expected.Top, "The fixed launch preset round-trips with full numeric precision");
        string path = Path.Combine(state, "settings.json");
        byte[] bytes = File.ReadAllBytes(path);
        Check(bytes.Length > 3 && !(bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) && !File.ReadAllText(path).Contains("DefaultDirectory"), "Settings use plain UTF-8 and do not serialize the static default directory");
        foreach (string culture in new[] { "en-US", "zh-CN", "fr-FR", "de-DE" }) {
            var previous = Thread.CurrentThread.CurrentCulture;
            try { Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture); saved = Settings.Load(state); }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
            Check(saved.InterfaceScale == expected.InterfaceScale && saved.WatchingWidth == expected.WatchingWidth, "Full-precision numeric settings load independently of culture " + culture);
        }

        saved.Left = 400; saved.Top = 100; saved.Width = 1100; saved.Height = 600; saved.InterfaceScale = 1.4; saved.Zoom = 1.3; saved.WatchingWidth = 260; saved.Save(state);
        var reopened = Settings.Load(state); reopened.Migrate(); reopened.ApplyLaunchLayout();
        Check(reopened.Left == expected.Left && reopened.Top == expected.Top && reopened.Width == expected.Width && reopened.Height == expected.Height && reopened.InterfaceScale == expected.InterfaceScale, "Transient moves and resizing never replace the fixed launch preset");
        Check(reopened.Zoom == 1.3 && reopened.WatchingWidth == 260, "Applying launch layout preserves independent zoom and column-width preferences");
        var preset = reopened.LaunchLayout; reopened.Left = 999; reopened.ApplyLaunchLayout();
        Check(ReferenceEquals(preset, reopened.LaunchLayout) && reopened.Left == expected.Left, "Repeated preset application does not capture later transient geometry");
        var legacy = Fixture(); legacy.ApplyLaunchLayout();
        Check(legacy.LaunchLayout != null && legacy.LaunchLayout.Width == expected.Width && legacy.LaunchLayout.InterfaceScale == expected.InterfaceScale, "A legacy settings object captures its existing geometry only once");
        var clamped = new Settings { LaunchLayout = new PanelLayout { InterfaceScale = double.NaN, Left = 12, Top = 13, Width = 800, Height = 400 } };
        clamped.ApplyLaunchLayout();
        Check(clamped.InterfaceScale == 1 && clamped.Left == 12 && clamped.Width == 800, "Preset scale uses the existing finite scale fallback without replacing its position");

        string corrupt = Path.Combine(output, "corrupt"); Directory.CreateDirectory(corrupt);
        string corruptPath = Path.Combine(corrupt, "settings.json");
        foreach (string text in new[] { "", "{", "null", "[]", "{\"Width\":\"not a number\"}" }) {
            File.WriteAllText(corruptPath, text);
            Reject<InvalidDataException>(delegate { Settings.Load(corrupt); }, "Existing invalid JSON raises an error instead of resetting: " + (text.Length == 0 ? "empty file" : text));
            Check(File.ReadAllText(corruptPath) == text, "A failed read leaves the invalid file untouched");
        }

        expected.Save(state);
        var blocked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = new Thread(delegate() { Thread.Sleep(60); blocked.Dispose(); }); release.Start();
        try { saved = Settings.Load(state); } finally { release.Join(); blocked.Dispose(); }
        Check(saved.Width == expected.Width && saved.InterfaceScale == expected.InterfaceScale, "A transient sharing violation is retried and returns the saved settings");
        var clock = Stopwatch.StartNew();
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Reject<IOException>(delegate { Settings.Load(state); }, "A persistently unreadable existing file raises its IO error instead of defaults");
        Check(clock.ElapsedMilliseconds < 2000, "IO retries remain bounded");
        string beforeFailure = File.ReadAllText(path);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject<IOException>(delegate { new Settings { Width = 1000 }.Save(state); }, "An atomic replacement blocked by an external reader reports failure");
        Check(File.ReadAllText(path) == beforeFailure && !Directory.GetFiles(state, "*.tmp").Any(), "Failed replacement keeps the previous valid settings and cleans its temporary file");

        string concurrent = Path.Combine(output, "concurrent");
        var a = new Settings { DesignVersion = 2, Width = 731, Height = 321, Left = 21, Top = 41, WatchingTitle = new string('A', 2000) };
        var b = new Settings { DesignVersion = 2, Width = 947, Height = 413, Left = 83, Top = 29, WatchingTitle = new string('B', 2000) };
        a.Save(concurrent);
        int stop = 0, reads = 0; Exception readerError = null; var gate = new object();
        var readers = Enumerable.Range(0, 3).Select(delegate(int index) {
            return new Thread(delegate() {
                try {
                    while (Volatile.Read(ref stop) == 0) {
                        var item = Settings.Load(concurrent);
                        bool completeA = item.Width == a.Width && item.Height == a.Height && item.Left == a.Left && item.Top == a.Top && item.WatchingTitle == a.WatchingTitle;
                        bool completeB = item.Width == b.Width && item.Height == b.Height && item.Left == b.Left && item.Top == b.Top && item.WatchingTitle == b.WatchingTitle;
                        if (!completeA && !completeB) throw new Exception("Concurrent reader observed partial or default settings: " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(item));
                        Interlocked.Increment(ref reads);
                    }
                } catch (Exception ex) { lock (gate) { if (readerError == null) readerError = ex; } }
            });
        }).ToArray();
        foreach (var reader in readers) reader.Start();
        try { for (int i = 0; i < 100; i++) (i % 2 == 0 ? b : a).Save(concurrent); }
        finally { Volatile.Write(ref stop, 1); foreach (var reader in readers) reader.Join(); }
        if (readerError != null) throw readerError;
        Check(reads > 0 && Settings.Load(concurrent).Width == a.Width, "Concurrent readers see complete old or new settings throughout 100 durable replacements (" + reads + " reads)");
        Check(!Directory.GetFiles(concurrent, "*.tmp").Any(), "Successful atomic saves leave no temporary files");
    }
    static int Main(string[] args) {
        try { Run(Path.GetFullPath(args[0])); Console.WriteLine(checks + " settings persistence checks passed without UI."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
