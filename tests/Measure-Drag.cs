using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

// This records native window-position cadence, not displayed frames or FPS.
// Run only against a disposable test instance, never the user's resident app.
public static class DragMeasurement {
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Position; public uint Private; }
    public sealed class PositionSample { public double Ms; public int X, Y; }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point position);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint flags);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);

    static void Pump() { Message message; while (PeekMessage(out message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); } }
    static void WaitWithMessages(int milliseconds) {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds) { Pump(); Thread.Sleep(10); }
    }
    static void Activate(IntPtr target) {
        // Only the isolated fixture reaches this helper. Show the exact HWND,
        // without activation first, so a denied foreground request can safely
        // fall back to a normal mouse click in the verified blank header.
        ShowWindow(target, 4);
        SetWindowPos(target, new IntPtr(-1), 0, 0, 0, 0, 0x53);
        WaitWithMessages(100);
        uint ignored, current = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
        bool attached = foreground != 0 && foreground != current && AttachThreadInput(current, foreground, true);
        try { SetForegroundWindow(target); }
        finally { if (attached) AttachThreadInput(current, foreground, false); }
        for (int attempt = 0; attempt < 25 && GetForegroundWindow() != target; attempt++) { Pump(); Thread.Sleep(20); }
        if (GetForegroundWindow() == target) return;
        Rect rectangle;
        if (!GetWindowRect(target, out rectangle)) throw new InvalidOperationException("Cannot read the isolated test header.");
        var header = new Point { X = rectangle.Left + (int)((rectangle.Right - rectangle.Left) * .45), Y = rectangle.Top + 50 };
        if (GetAncestor(WindowFromPoint(header), 2) != target) throw new InvalidOperationException("The isolated test header is covered; activation click cancelled.");
        SetCursorPos(header.X, header.Y);
        bool down = false;
        try {
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); down = true;
            WaitWithMessages(60);
        } finally { if (down) mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
        // A second header press inside the system double-click interval would
        // maximize the app instead of starting a drag.
        WaitWithMessages((int)Math.Max(500, GetDoubleClickTime() + 80));
        if (GetForegroundWindow() != target) throw new InvalidOperationException("Cannot activate the isolated test window, including a verified header click.");
    }
    static double Rounded(double value) { return Math.Round(value, 3); }
    static object Intervals(List<PositionSample> samples) {
        var gaps = new List<double>();
        for (int i = 1; i < samples.Count; i++) gaps.Add(samples[i].Ms - samples[i - 1].Ms);
        gaps.Sort();
        if (gaps.Count == 0) return new { Count = 0, P50Ms = 0.0, P95Ms = 0.0, MaxMs = 0.0, Over50Ms = 0 };
        int over = 0; foreach (double gap in gaps) if (gap > 50) over++;
        return new { Count = gaps.Count, P50Ms = Rounded(gaps[(int)Math.Ceiling(gaps.Count * .50) - 1]), P95Ms = Rounded(gaps[(int)Math.Ceiling(gaps.Count * .95) - 1]), MaxMs = Rounded(gaps[gaps.Count - 1]), Over50Ms = over };
    }
    static int Travel(int roomBefore, int roomAfter, int maximum) {
        return roomAfter >= roomBefore ? Math.Max(0, Math.Min(maximum, roomAfter)) : -Math.Max(0, Math.Min(maximum, roomBefore));
    }

    [STAThread] public static int Main(string[] args) {
        Point originalCursor = new Point(); bool haveCursor = false, mouseDown = false, timerPeriod = false;
        try {
            if (args.Length != 2) throw new ArgumentException("Usage: Measure-Drag.exe <native-window-handle> <output-json>");
            SetProcessDPIAware();
            IntPtr target = new IntPtr(long.Parse(args[0]));
            if (!IsWindow(target)) throw new ArgumentException("The target window no longer exists.");
            if ((GetAsyncKeyState(1) & 0x8000) != 0) throw new InvalidOperationException("Release the mouse button before the drag test.");
            haveCursor = GetCursorPos(out originalCursor);
            Activate(target);
            Rect initial; if (!GetWindowRect(target, out initial)) throw new InvalidOperationException("Cannot read test window bounds.");
            var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromWindow(target, 2), ref monitor)) throw new InvalidOperationException("Cannot read the test monitor work area.");
            var start = new Point { X = initial.Left + (int)((initial.Right - initial.Left) * .45), Y = initial.Top + 50 };
            if (GetAncestor(WindowFromPoint(start), 2) != target) throw new InvalidOperationException("The test header is covered by another window.");
            int travelX = Travel(initial.Left - monitor.Work.Left - 24, monitor.Work.Right - initial.Right - 24, 280);
            int travelY = Travel(initial.Top - monitor.Work.Top - 24, monitor.Work.Bottom - initial.Bottom - 24, 130);
            if (Math.Abs(travelX) < 80 || Math.Abs(travelY) < 40) throw new InvalidOperationException("The test window needs more room for a bounded two-axis drag.");
            uint processId; GetWindowThreadProcessId(target, out processId);
            using (var process = Process.GetProcessById((int)processId)) {
                IntPtr material = GetWindow(target, 4);
                var inputs = new List<PositionSample>(); var positions = new List<PositionSample>();
                var polls = new List<PositionSample>();
                int misaligned = 0, maximumMaterialOffset = 0;
                timerPeriod = timeBeginPeriod(1) == 0;
                SetCursorPos(start.X, start.Y); Thread.Sleep(120);
                process.Refresh(); TimeSpan cpuBefore = process.TotalProcessorTime;
                var clock = Stopwatch.StartNew();
                mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouseDown = true;
                Rect previous = initial; int sent = 0; double nextInput = 16, nextPoll = 0;
                const int sampleCount = 180; const double intervalMs = 16;
                while (clock.Elapsed.TotalMilliseconds < sampleCount * intervalMs + 150) {
                    Pump(); double now = clock.Elapsed.TotalMilliseconds;
                    if (sent < sampleCount && now >= nextInput) {
                        if (GetForegroundWindow() != target) throw new InvalidOperationException("Test interrupted because the foreground window changed.");
                        double t = (sent + 1.0) / sampleCount;
                        // A constant-speed rectangular loop changes position at
                        // every input; stationary rounded endpoints would look
                        // like application stalls in the cadence measurement.
                        double phase = t * 4;
                        double px = phase < 1 ? phase : phase < 2 ? 1 : phase < 3 ? 3 - phase : 0;
                        double py = phase < 1 ? 0 : phase < 2 ? phase - 1 : phase < 3 ? 1 : 4 - phase;
                        int x = start.X + (int)Math.Round(travelX * px);
                        int y = start.Y + (int)Math.Round(travelY * py);
                        SetCursorPos(x, y);
                        inputs.Add(new PositionSample { Ms = Rounded(clock.Elapsed.TotalMilliseconds), X = x, Y = y });
                        sent++; nextInput += intervalMs;
                    }
                    if (now >= nextPoll) {
                        Rect rectangle;
                        if (!GetWindowRect(target, out rectangle)) throw new InvalidOperationException("The test window closed during dragging.");
                        polls.Add(new PositionSample { Ms = Rounded(clock.Elapsed.TotalMilliseconds), X = rectangle.Left, Y = rectangle.Top });
                        if (rectangle.Left != previous.Left || rectangle.Top != previous.Top) {
                            positions.Add(new PositionSample { Ms = Rounded(clock.Elapsed.TotalMilliseconds), X = rectangle.Left, Y = rectangle.Top });
                            previous = rectangle;
                        }
                        Rect materialRect;
                        if (material != IntPtr.Zero && GetWindowRect(material, out materialRect)) {
                            int offset = Math.Max(Math.Abs(materialRect.Left - rectangle.Left), Math.Abs(materialRect.Top - rectangle.Top));
                            if (offset > 0) misaligned++;
                            maximumMaterialOffset = Math.Max(maximumMaterialOffset, offset);
                        }
                        nextPoll = now + 2;
                    }
                    Thread.Sleep(1);
                }
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); mouseDown = false;
                double elapsed = clock.Elapsed.TotalMilliseconds;
                process.Refresh(); double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                if (inputs.Count != sampleCount) throw new InvalidOperationException("The input schedule could not finish; this run is not comparable.");
                if (positions.Count < 20) throw new InvalidOperationException("Too few native position changes: " + positions.Count + "; initial bounds " + initial.Left + "," + initial.Top + "," + initial.Right + "," + initial.Bottom + "; final position " + previous.Left + "," + previous.Top + ". The header drag did not run correctly.");
                var result = new {
                    Metric = "Native window-position cadence proxy; not rendered-frame timing or FPS",
                    ProcessId = processId, DurationMs = Rounded(elapsed), TargetInputIntervalMs = intervalMs,
                    InputSamples = inputs.Count, NativePositionUpdates = positions.Count, NativePolls = polls.Count,
                    InputIntervals = Intervals(inputs), NativePositionIntervals = Intervals(positions), PollIntervals = Intervals(polls),
                    ApplicationCpuMs = Rounded(cpuMs), MaterialMisalignedPolls = misaligned, MaximumMaterialOffsetPx = maximumMaterialOffset,
                    PathTravelX = travelX, PathTravelY = travelY, Inputs = inputs, Positions = positions
                };
                File.WriteAllText(Path.GetFullPath(args[1]), new JavaScriptSerializer().Serialize(result));
                return 0;
            }
        } catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        finally {
            if (mouseDown) mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            if (haveCursor) SetCursorPos(originalCursor.X, originalCursor.Y);
            if (timerPeriod) timeEndPeriod(1);
        }
    }
}
