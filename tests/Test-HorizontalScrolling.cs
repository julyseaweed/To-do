using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shike;

// Uses the real native HWND and SendMessage, with the entire fixture kept
// outside the desktop. It does not simulate or measure physical finger input.
static class HorizontalScrollingTests {
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    static int checks, failures;
    static MainWindow window;
    static IntPtr hwnd;
    static void Check(bool pass, string label) { checks++; if (!pass) failures++; Console.WriteLine((pass ? "PASS " : "FAIL ") + label); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject value) {
        yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(value, i))) yield return child;
    }
    static T Named<T>(string name) where T : DependencyObject {
        var found = Descendants(window).OfType<T>().FirstOrDefault(x => AutomationProperties.GetName(x) == name);
        if (found == null) throw new InvalidOperationException("Missing control: " + name);
        return found;
    }
    static void Pump() {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(35) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); window.UpdateLayout();
    }
    static Point ScreenPoint(FrameworkElement element) { return element.PointToScreen(new Point(element.ActualWidth * .4, Math.Min(100, element.ActualHeight * .35))); }
    static void Wheel(Point point, int delta, int message = 0x020E) {
        int x = (int)Math.Round(point.X), y = (int)Math.Round(point.Y);
        if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue) throw new InvalidOperationException("Fixture coordinates cannot be encoded in a Windows wheel message.");
        long position = unchecked((ushort)x) | ((long)unchecked((ushort)y) << 16);
        SendMessage(hwnd, message, new IntPtr((long)unchecked((ushort)delta) << 16), new IntPtr(position));
    }
    static void Offset(ScrollViewer topics, double value) { topics.ScrollToHorizontalOffset(value); Pump(); }
    [STAThread] static int Main(string[] args) {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try {
            string state = Path.Combine(Path.GetTempPath(), "shike-horizontal-scroll-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(state);
            var store = new NoteStore(state, Path.Combine(state, "backups"));
            var notes = new StringBuilder("# Native scrolling fixture\n\n");
            foreach (string group in new[] { NoteStore.ReadingGroup, "First topic", "Second topic", "Third topic", "Fourth topic", "Fifth topic", "Sixth topic" }) {
                notes.Append("## ").Append(group).Append("\n");
                for (int i = 0; i < 24; i++) notes.Append("- [ ] ").Append(group).Append(" item ").Append(i + 1).Append("\n");
                notes.Append("\n");
            }
            File.WriteAllText(store.CurrentPath, notes.ToString());
            window = new MainWindow(store, new Settings { Width = 720, Height = 500, Pinned = false, Transparency = 45 }, state);
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Left = SystemParameters.VirtualScreenLeft - 3000; window.Top = SystemParameters.VirtualScreenTop - 2000;
            foreach (string name in new[] { "watcher", "contrastTimer", "toastTimer" }) {
                var timer = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window) as DispatcherTimer;
                if (timer != null) timer.Stop();
            }
            window.Show(); Pump(); hwnd = new WindowInteropHelper(window).Handle;
            NativeRect bounds; if (!GetWindowRect(hwnd, out bounds)) throw new InvalidOperationException("No native fixture window.");
            if (MonitorFromRect(ref bounds, 0) != IntPtr.Zero) throw new InvalidOperationException("The native fixture must stay outside every monitor.");
            Check(true, "Native fixture remains entirely outside every desktop monitor");
            var topics = Named<ScrollViewer>("Topics");
            var watching = Named<ScrollViewer>("Items in " + NoteStore.ReadingGroup);
            var first = Named<ScrollViewer>("Items in First topic");
            Check(topics.ScrollableWidth > 500, "Real topic strip has horizontal overflow for native-input testing");
            watching.ScrollToVerticalOffset(64); first.ScrollToVerticalOffset(48); Pump();
            double watchOffset = watching.VerticalOffset, firstOffset = first.VerticalOffset;
            double watchingX = watching.PointToScreen(new Point()).X;
            Point topicPoint = ScreenPoint(topics);
            var horizontalBar = (ScrollBar)topics.Template.FindName("PART_HorizontalScrollBar", topics);
            var horizontalTrack = (Track)horizontalBar.Template.FindName("PART_Track", horizontalBar);
            Offset(topics, 0);
            horizontalTrack.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            horizontalTrack.Thumb.RaiseEvent(new DragDeltaEventArgs(45, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Pump();
            Check(topics.HorizontalOffset > 0, "Dragging the actual horizontal thumb moves the topic content");
            horizontalTrack.Thumb.RaiseEvent(new DragCompletedEventArgs(45, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Pump();
            Offset(topics, 200);
            var barWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            horizontalBar.RaiseEvent(barWheel); Pump();
            Check(barWheel.Handled && topics.HorizontalOffset > 200, "Ordinary wheel input over the bottom bar scrolls horizontally");
            Check(horizontalTrack.Thumb.ActualHeight >= 10, "Horizontal thumb keeps a usable invisible hit area around its thin line");
            Offset(topics, 100);
            var pageRight = horizontalTrack.IncreaseRepeatButton;
            var pageLeft = horizontalTrack.DecreaseRepeatButton;
            Check(pageRight != null && pageLeft != null, "Empty track space has paging controls on both sides of the thumb");
            if (pageRight != null && pageLeft != null) {
                ((RoutedCommand)pageRight.Command).Execute(null, pageRight); Pump();
                double paged = topics.HorizontalOffset;
                Check(paged > 100, "Clicking the track to the right moves to later topics");
                ((RoutedCommand)pageLeft.Command).Execute(null, pageLeft); Pump();
                Check(topics.HorizontalOffset < paged, "Clicking the track to the left moves back");
            }
            Check(Math.Abs(watching.VerticalOffset - watchOffset) < .1 && Math.Abs(first.VerticalOffset - firstOffset) < .1, "Bottom-bar interactions leave column scroll positions unchanged");
            Offset(topics, 0); Wheel(topicPoint, 120); Pump();
            double full = topics.HorizontalOffset;
            Check(full > 0, "Positive WM_MOUSEHWHEEL moves topics right without the scrollbar");
            Wheel(topicPoint, -60); Pump();
            Check(topics.HorizontalOffset > 0 && topics.HorizontalOffset < full, "Signed negative wheel delta moves topics left proportionally");
            Check(Math.Abs(watching.VerticalOffset - watchOffset) < .1 && Math.Abs(first.VerticalOffset - firstOffset) < .1, "Horizontal gestures preserve both independent vertical offsets");
            Check(Math.Abs(watching.PointToScreen(new Point()).X - watchingX) < .1, "Watching list stays fixed while the topics move");
            Offset(topics, 200); Wheel(topicPoint, 1); Pump();
            double unit = topics.HorizontalOffset - 200;
            Check(unit > .01 && unit < 4, "A precision delta of one scrolls immediately without waiting for120");
            double beforeBurst = topics.HorizontalOffset;
            for (int i = 0; i < 120; i++) Wheel(topicPoint, 1);
            Pump();
            Check(Math.Abs((topics.HorizontalOffset - beforeBurst) - unit * 120) < 1.1, "A burst of120 small native messages retains every fractional delta");
            Wheel(topicPoint, short.MaxValue); Pump();
            Check(Math.Abs(topics.HorizontalOffset - topics.ScrollableWidth) < .1, "Large positive input clamps at the right boundary");
            Wheel(topicPoint, 120); Wheel(topicPoint, -1); Pump();
            Check(topics.HorizontalOffset < topics.ScrollableWidth, "Reversing at the right boundary responds without accumulated overscroll");
            Wheel(topicPoint, short.MinValue); Pump();
            Check(topics.HorizontalOffset == 0, "Large negative input clamps at the left boundary");
            Offset(topics, 130); Wheel(topicPoint, 1); Pump();
            Check(Math.Abs(topics.HorizontalOffset - (130 + unit)) < .1, "Wheel scrolling resumes from a manually changed scrollbar position");
            double ignored = topics.HorizontalOffset;
            Wheel(ScreenPoint(watching), 120); Pump();
            Check(Math.Abs(topics.HorizontalOffset - ignored) < .1, "A horizontal gesture over Watching does not move the separate topic strip");
            Wheel(window.PointToScreen(new Point(50, 20)), 120); Pump();
            Check(Math.Abs(topics.HorizontalOffset - ignored) < .1, "Window-header gestures outside the board are ignored");
            Wheel(topicPoint, -120, 0x020A); Pump();
            Check(Math.Abs(topics.HorizontalOffset - ignored) < .1, "Native vertical-wheel messages are not converted into horizontal scrolling");
            Offset(topics, 0); first.ScrollToVerticalOffset(0); Pump();
            window.Edit(store.Load().Entries.First(x => x.Group == "First topic"), "First topic"); Pump();
            var editor = Named<TextBox>("Item text");
            Check(((HwndSource)PresentationSource.FromVisual(editor)).Handle == hwnd, "Inline WPF editors receive input through the same native HWND");
            double editingOffset = topics.HorizontalOffset;
            Wheel(ScreenPoint(editor), 120); Pump();
            Check(topics.HorizontalOffset > editingOffset, "Horizontal wheel input works directly over an inline text editor");
            typeof(MainWindow).GetMethod("CancelInline", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null); Pump();
            typeof(MainWindow).GetMethod("Zoom", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { .2 }); Pump();
            Offset(topics, 0); Wheel(ScreenPoint(topics), 120); Pump();
            Check(topics.HorizontalOffset > 0, "Hit testing remains correct after board zoom on negative screen coordinates");
            Console.WriteLine("Horizontal native checks: " + checks + ", failures: " + failures + ". Isolated vault: " + state);
            return failures == 0 ? 0 : 1;
        } catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        finally { if (window != null) window.Exit(); app.Shutdown(); }
    }
}
