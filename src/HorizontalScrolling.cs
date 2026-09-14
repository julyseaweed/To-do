using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Shike {
    public sealed partial class MainWindow {
        HwndSource horizontalWheelSource;
        DispatcherOperation horizontalWheelOperation;
        double horizontalWheelTarget;
        bool horizontalWheelHasTarget;

        void EnableHorizontalScrolling() {
            if (horizontalWheelSource != null) return;
            horizontalWheelSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (horizontalWheelSource == null) return;
            horizontalWheelSource.AddHook(HorizontalWheelMessage);
            scroll.PreviewMouseWheel += HorizontalBarWheel;
            scroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e) {
                // Nested lists bubble their vertical ScrollChanged events.
                if (e.OriginalSource != scroll || horizontalWheelOperation != null) return;
                if (e.HorizontalChange != 0 || e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0) {
                    horizontalWheelTarget = scroll.HorizontalOffset;
                    horizontalWheelHasTarget = true;
                }
            };
            Closed += delegate {
                if (horizontalWheelOperation != null) { horizontalWheelOperation.Abort(); horizontalWheelOperation = null; }
                if (horizontalWheelSource != null && !horizontalWheelSource.IsDisposed) horizontalWheelSource.RemoveHook(HorizontalWheelMessage);
                horizontalWheelSource = null;
            };
        }

        void HorizontalBarWheel(object sender, MouseWheelEventArgs e) {
            if (e.Handled || collapsed || scroll.ScrollableWidth <= 0) return;
            // A wheel over a horizontal bar follows that bar's axis. Wheels
            // over cards keep each column's independent vertical scrolling.
            for (var node = e.OriginalSource as DependencyObject; node != null && node != scroll;
                 node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)) {
                var bar = node as ScrollBar;
                if (bar == null || bar.Orientation != Orientation.Horizontal || bar.TemplatedParent != scroll) continue;
                ScrollTopicsBy(-e.Delta * 0.6);
                e.Handled = true;
                return;
            }
        }

        IntPtr HorizontalWheelMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
            const int WM_MOUSEHWHEEL = 0x020E;
            if (message != WM_MOUSEHWHEEL || collapsed || scroll == null || !scroll.IsVisible || scroll.ScrollableWidth <= 0) return IntPtr.Zero;
            long position = lParam.ToInt64();
            // Both coordinates are signed screen pixels, including monitors
            // left of the primary display. PointFromScreen handles DPI/zoom.
            var screenPoint = new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff));
            Point point;
            try { point = scroll.PointFromScreen(screenPoint); }
            catch (InvalidOperationException) { return IntPtr.Zero; }
            if (!new Rect(0, 0, scroll.ActualWidth, scroll.ActualHeight).Contains(point)) return IntPtr.Zero;
            int delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
            if (delta == 0) return IntPtr.Zero;
            ScrollTopicsBy(delta * 0.6);
            handled = true;
            return IntPtr.Zero;
        }

        void ScrollTopicsBy(double distance) {
            if (!horizontalWheelHasTarget) horizontalWheelTarget = scroll.HorizontalOffset;
            horizontalWheelHasTarget = true;
            // Precision touchpads can send delta=1. Preserve those fractions,
            // and accumulate bursts before WPF applies its deferred offset.
            // Windows already applies the user's natural-scroll direction.
            horizontalWheelTarget = Math.Max(0, Math.Min(scroll.ScrollableWidth, horizontalWheelTarget + distance));
            if (horizontalWheelOperation == null) {
                horizontalWheelOperation = Dispatcher.BeginInvoke(new Action(delegate {
                    horizontalWheelOperation = null;
                    if (collapsed || !scroll.IsVisible) { horizontalWheelHasTarget = false; return; }
                    scroll.ScrollToHorizontalOffset(horizontalWheelTarget);
                }), DispatcherPriority.Input);
            }
        }
    }
}
