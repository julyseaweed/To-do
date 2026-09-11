using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shike;

// Tests the real menu handlers and dispatcher debounce without opening a popup
// or creating a visible window. Every settings write uses an isolated directory.
static class TransparencyTests {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static MainWindow window;
    static int checks;
    static object Field(string name) { return typeof(MainWindow).GetField(name, Hidden).GetValue(window); }
    static object Call(string name) { return typeof(MainWindow).GetMethod(name, Hidden).Invoke(window, null); }
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static void Pump(int milliseconds) {
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        stop.Tick += delegate { stop.Stop(); frame.Continue = false; };
        stop.Start(); Dispatcher.PushFrame(frame);
    }
    static LinearGradientBrush Surface { get { return (LinearGradientBrush)((Border)Field("surface")).Background; } }
    static void CloseMenu(ContextMenu menu) { menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent)); }
    [STAThread] static int Main(string[] args) {
        Application app = null; ContextMenu menu = null;
        try {
            string output = Path.GetFullPath(args[0]);
            Check(Settings.ClampTransparency(35) == 35 && Settings.ClampTransparency(95) == 95, "Transparency accepts both supported endpoints, 35 and 95 percent");
            Check(Settings.ClampTransparency(-40) == 35 && Settings.ClampTransparency(130) == 95, "Out-of-range transparency clamps to the supported endpoints");
            Check(Settings.ClampTransparency(double.NaN) == 45 && Settings.ClampTransparency(double.PositiveInfinity) == 45 && Settings.ClampTransparency(double.NegativeInfinity) == 45, "Nonfinite transparency falls back to 45 percent");
            string legacyPath = Path.Combine(output, "legacy"); Directory.CreateDirectory(legacyPath);
            File.WriteAllText(Path.Combine(legacyPath, "settings.json"), "{\"DesignVersion\":2,\"Transparency\":96,\"WatchingTitle\":\"Saved label\",\"WatchingWidth\":310,\"Width\":950,\"Pinned\":true}");
            var legacy = Settings.Load(legacyPath); legacy.Migrate();
            Check(legacy.Transparency == 95 && legacy.WatchingTitle == "Saved label" && legacy.WatchingWidth == 310 && legacy.Width == 950 && legacy.Pinned, "Migrating an old 96 percent setting clamps it to 95 without changing layout, labels or pinning");
            var invalid = new Settings { DesignVersion = 2, Transparency = double.NaN }; invalid.Migrate();
            Check(invalid.Transparency == 45, "Migration normalizes a nonfinite setting");

            string state = Path.Combine(output, "state");
            var store = new NoteStore(Path.Combine(output, "vault"), Path.Combine(output, "backups")); store.Initialize();
            string originalNote = File.ReadAllText(store.CurrentPath);
            var settings = new Settings { DesignVersion = 2, Width = 800, Height = 480, Transparency = 96, WatchingTitle = "Saved label", WatchingWidth = 310 };
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            window = new MainWindow(store, settings, state);
            Check(settings.Transparency == 95, "Window construction also clamps an unmigrated 96 percent setting");
            settings.Transparency = 45; settings.Save(state);
            typeof(MainWindow).GetField("blurAvailable", Hidden).SetValue(window, true); Call("ApplyGlass");
            menu = (ContextMenu)Call("MoreMenu");
            var container = (MenuItem)menu.Items[0]; var controls = (StackPanel)container.Header;
            var label = controls.Children.OfType<TextBlock>().Single(); var slider = controls.Children.OfType<Slider>().Single();
            Check(slider.Minimum == 35 && slider.Maximum == 95 && slider.Value == 45 && AutomationProperties.GetName(slider) == "Glass transparency", "The real menu exposes the accessible slider with the saved value and supported range");
            Check(container.StaysOpenOnClick && slider.IsMoveToPointEnabled && slider.IsSnapToTickEnabled && slider.SmallChange == 1, "Slider interactions stay in the menu and support direct positioning and one-percent steps");
            Check(label.Text == "Transparency  45%" && !menu.IsOpen && !window.IsVisible, "The initial label matches the saved setting without opening any test UI");

            slider.Value = 35;
            var opaque = Surface;
            Check(settings.Transparency == 35 && label.Text == "Transparency  35%", "Moving the slider immediately updates the in-memory setting and visible percentage label");
            slider.Value = 95;
            var transparent = Surface;
            Check(!ReferenceEquals(opaque, transparent) && transparent.GradientStops.Max(x => x.Color.A) < opaque.GradientStops.Min(x => x.Color.A), "The real ValueChanged handler immediately applies lower-opacity glass at a higher transparency value");
            Check(transparent.GradientStops.All(x => x.Color.A > 0) && transparent.IsFrozen, "Maximum transparency retains a nonzero glass tint in a frozen brush");
            Check(Settings.Load(state).Transparency == 45, "Immediate slider updates do not write settings before the debounce expires");

            slider.Value = 70; Pump(80);
            Check(Settings.Load(state).Transparency == 45, "A short pause while dragging does not save intermediate values");
            slider.Value = 80; Pump(140);
            Check(Settings.Load(state).Transparency == 45, "A new slider value restarts the 200-millisecond save debounce");
            Pump(160);
            Check(Settings.Load(state).Transparency == 80, "After the debounce the latest slider value is durably saved");

            slider.Value = 60;
            Check(Settings.Load(state).Transparency == 80, "A new adjustment remains pending before menu closure");
            CloseMenu(menu);
            Check(Settings.Load(state).Transparency == 60 && label.Text == "Transparency  60%", "The actual Closed handler immediately flushes the pending slider value");
            string settingsPath = Path.Combine(state, "settings.json");
            DateTime sentinel = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc); File.SetLastWriteTimeUtc(settingsPath, sentinel);
            Pump(300); CloseMenu(menu);
            Check(File.GetLastWriteTimeUtc(settingsPath) == sentinel, "Closing the menu stops the pending timer and repeated closure does not write again");
            var restored = Settings.Load(state);
            Check(restored.WatchingTitle == "Saved label" && restored.WatchingWidth == 310 && !restored.Pinned && File.ReadAllText(store.CurrentPath) == originalNote, "Transparency edits preserve list data, column preferences and pinning");
            var reopened = (ContextMenu)Call("MoreMenu");
            var reopenedControls = (StackPanel)((MenuItem)reopened.Items[0]).Header;
            Check(reopenedControls.Children.OfType<Slider>().Single().Value == 60 && reopenedControls.Children.OfType<TextBlock>().Single().Text == "Transparency  60%", "A newly created menu reflects the last persisted adjustment");
            CloseMenu(reopened);
            Console.WriteLine(checks + " transparency checks passed without showing a window or popup."); return 0;
        } catch (Exception ex) { for (; ex != null; ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1; }
        finally { if (menu != null) CloseMenu(menu); if (window != null) window.Close(); if (app != null) app.Shutdown(); }
    }
}
