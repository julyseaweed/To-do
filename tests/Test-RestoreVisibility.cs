using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Shike;

// Observe native show transactions synchronously, including the separate glass
// HWND. Checking only the final rectangle would miss a flash at stale bounds.
class RestoreVisibilityTests {
    const uint Changing = 0x46, Changed = 0x47, Show = 0x40, NoSize = 1, NoMove = 2;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Window, After; public int X, Y, Width, Height; public uint Flags; }
    delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc callback, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint message, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    static readonly SubclassProc hook = Observe;
    static readonly UIntPtr hookId = new UIntPtr(9071);
    static readonly Dictionary<IntPtr, string> handles = new Dictionary<IntPtr, string>();
    static readonly List<Observation> observations = new List<Observation>();
    static readonly List<string> csv = new List<string> { "case,window,phase,x,y,width,height,scale" };
    static MainWindow window;
    static Settings settings;
    static Rect presetBounds;
    static string activeCase;
    static int checks, failures;
    sealed class Observation { public string Case, Window; public uint Phase; public Rect Bounds; public double Scale; }
    static Rect Bounds(IntPtr hwnd) {
        NativeRect rect;
        if (!GetWindowRect(hwnd, out rect)) throw new InvalidOperationException("Cannot inspect test HWND.");
        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    static IntPtr Observe(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp, UIntPtr id, UIntPtr data) {
        // Never change or suppress a message. Read WINDOWPOS after downstream
        // callbacks have had the opportunity to finalize its proposed bounds.
        IntPtr result = DefSubclassProc(hwnd, message, wp, lp);
        if (activeCase != null && (message == Changing || message == Changed)) {
            var position = (WindowPosition)Marshal.PtrToStructure(lp, typeof(WindowPosition));
            if ((position.Flags & Show) != 0) {
                Rect rect = Bounds(hwnd);
                if (message == Changing) rect = new Rect(
                    (position.Flags & NoMove) != 0 ? rect.Left : position.X,
                    (position.Flags & NoMove) != 0 ? rect.Top : position.Y,
                    (position.Flags & NoSize) != 0 ? rect.Width : position.Width,
                    (position.Flags & NoSize) != 0 ? rect.Height : position.Height);
                observations.Add(new Observation { Case = activeCase, Window = handles[hwnd], Phase = message, Bounds = rect, Scale = settings.InterfaceScale });
            }
        }
        return result;
    }
    static bool Same(Rect first, Rect second) {
        return Math.Abs(first.Left - second.Left) <= 1 && Math.Abs(first.Top - second.Top) <= 1 &&
               Math.Abs(first.Width - second.Width) <= 1 && Math.Abs(first.Height - second.Height) <= 1;
    }
    static string N(double value) { return value.ToString("0.####", CultureInfo.InvariantCulture); }
    static string Describe(Rect rect) { return N(rect.Left) + "," + N(rect.Top) + " " + N(rect.Width) + "x" + N(rect.Height); }
    static void Check(bool success, string message) {
        checks++; if (!success) failures++;
        Console.WriteLine((success ? "PASS " : "FAIL ") + message);
    }
    static void Pump() { window.Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.ApplicationIdle); }
    static void Call(string method) { typeof(MainWindow).GetMethod(method, Private).Invoke(window, null); }
    static void Attach(IntPtr hwnd, string name) {
        if (hwnd == IntPtr.Zero || !SetWindowSubclass(hwnd, hook, hookId, UIntPtr.Zero)) throw new InvalidOperationException("Cannot observe " + name + " HWND.");
        handles.Add(hwnd, name);
    }
    static void MoveAway(bool resize) {
        var work = SystemParameters.WorkArea;
        if (resize) { window.Width += 55; window.Height += 35; settings.InterfaceScale = 1; }
        window.Left = work.Right - window.Width - 16; window.Top = work.Top + 24;
        window.UpdateLayout(); Pump();
        Check(!Same(Bounds(new WindowInteropHelper(window).Handle), presetBounds), "Fixture is visibly different from the launch preset");
    }
    static void RestoreCase(string name, bool hide, string stateDirectory, string notesPath, string originalNotes) {
        if (hide) {
            window.HidePanel(); Pump();
            Check(handles.Keys.All(x => !IsWindowVisible(x)), name + ": content and glass are hidden before restore");
        }
        observations.Clear(); activeCase = name;
        try { window.Restore(); Pump(); }
        finally { activeCase = null; }
        foreach (string host in handles.Values) {
            foreach (uint phase in new[] { Changing, Changed }) {
                var shown = observations.Where(x => x.Window == host && x.Phase == phase).ToArray();
                string stage = phase == Changing ? "show proposal" : "show commit";
                Check(shown.Length > 0, name + ": " + host + " has an observed " + stage);
                if (shown.Length > 0) {
                    Check(shown.All(x => Same(x.Bounds, presetBounds)), name + ": " + host + " " + stage + " uses preset bounds immediately; first=" + Describe(shown[0].Bounds));
                    Check(shown.All(x => Math.Abs(x.Scale - settings.LaunchLayout.InterfaceScale) < .000001), name + ": " + host + " " + stage + " has the preset scale");
                }
            }
        }
        foreach (var seen in observations) csv.Add(string.Join(",", new[] { seen.Case, seen.Window, seen.Phase == Changing ? "changing" : "changed", N(seen.Bounds.Left), N(seen.Bounds.Top), N(seen.Bounds.Width), N(seen.Bounds.Height), N(seen.Scale) }));
        Check(handles.Keys.All(x => IsWindowVisible(x) && Same(Bounds(x), presetBounds)), name + ": both final native bounds match the preset");
        var saved = Settings.Load(stateDirectory);
        var preset = settings.LaunchLayout;
        Check(saved.LaunchLayout.Left == preset.Left && saved.LaunchLayout.Top == preset.Top && saved.LaunchLayout.Width == preset.Width && saved.LaunchLayout.Height == preset.Height && saved.LaunchLayout.InterfaceScale == preset.InterfaceScale, name + ": launch preset is unchanged");
        Check(File.ReadAllText(notesPath) == originalNotes, name + ": notes remain byte-for-byte unchanged");
    }
    [STAThread] static int Main(string[] args) {
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            string state = Path.Combine(output, "state"); Directory.CreateDirectory(state);
            var store = new NoteStore(Path.Combine(output, "vault"), Path.Combine(output, "backups")); store.Initialize();
            string fixture = File.ReadAllText(store.CurrentPath);
            var work = SystemParameters.WorkArea;
            double width = Math.Min(740, work.Width - 64), height = Math.Min(330, work.Height - 80);
            settings = new Settings { Vault = store.Vault, DesignVersion = 2, Pinned = false, LaunchLayout = new PanelLayout { Left = work.Left + 16, Top = work.Bottom - height - 16, Width = width, Height = height, InterfaceScale = .85 } };
            settings.ApplyLaunchLayout(); settings.Save(state);
            window = new MainWindow(store, settings, state); window.Show(); Pump();
            IntPtr main = new WindowInteropHelper(window).Handle, glass = GetWindow(main, 4);
            var title = new StringBuilder(100); GetWindowText(glass, title, title.Capacity);
            if (title.ToString() != "To-do glass") throw new InvalidOperationException("The native glass HWND is unavailable; this test requires real glass rendering.");
            Attach(main, "content"); Attach(glass, "glass");
            presetBounds = Bounds(main);
            Console.WriteLine("PRESET " + Describe(presetBounds));
            Check(Same(Bounds(glass), presetBounds), "Initial content and glass have matching native bounds");
            MoveAway(false); RestoreCase("hidden-expanded", true, state, store.CurrentPath, fixture);
            MoveAway(true); RestoreCase("hidden-resized", true, state, store.CurrentPath, fixture);
            MoveAway(true); Call("ToggleCollapsed"); Call("StopDockAnimation");
            RestoreCase("hidden-collapsed", true, state, store.CurrentPath, fixture);
            MoveAway(false); Call("ToggleCollapsed"); Call("StopDockAnimation");
            RestoreCase("visible-collapsed", false, state, store.CurrentPath, fixture);
            MoveAway(false); RestoreCase("repeated-hidden-expanded", true, state, store.CurrentPath, fixture);
            Console.WriteLine(checks + " restore visibility checks; " + failures + " failure(s).");
            return failures == 0 ? 0 : 1;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally {
            activeCase = null;
            foreach (var handle in handles.Keys) RemoveWindowSubclass(handle, hook, hookId);
            if (window != null) window.Exit();
            if (Application.Current != null) Application.Current.Shutdown();
            File.WriteAllLines(Path.Combine(output, "native-show-transactions.csv"), csv);
            GC.KeepAlive(hook);
        }
    }
}
