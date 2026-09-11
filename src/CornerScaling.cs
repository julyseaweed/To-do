using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Shike {
    public sealed partial class MainWindow {
        // The glass border stays at one DIP. Only the finite content canvas
        // scales, so ScrollViewers keep their viewport and text keeps its wrap.
        const double ScaleBorder = 2;
        Viewbox scalingHost;
        Grid scalingCanvas, scalingViewport;
        bool cornerScaling;
        Size cornerInsets, cornerStartSize;
        double cornerStartScale;

        void ApplyScaledMinimum() {
            double scale = Math.Max(1, config.InterfaceScale);
            MinWidth = ScaleBorder + 338 * scale;
            MinHeight = ScaleBorder + 278 * scale;
        }

        Viewbox BuildScalingHost(Grid viewport) {
            scalingViewport = viewport;
            scalingCanvas = new Grid { UseLayoutRounding = false };
            layout.UseLayoutRounding = true;
            scalingCanvas.Children.Add(layout);
            scalingHost = new Viewbox {
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                UseLayoutRounding = false,
                Child = scalingCanvas
            };
            UpdateScalingViewport(new Size(Width, Height));
            viewport.SizeChanged += delegate(object sender, SizeChangedEventArgs e) {
                if (!collapsed && !cornerScaling)
                    UpdateScalingViewport(new Size(e.NewSize.Width + ScaleBorder, e.NewSize.Height + ScaleBorder));
            };
            return scalingHost;
        }

        void UpdateScalingViewport(Size outer) {
            if (outer.Width <= ScaleBorder || outer.Height <= ScaleBorder) return;
            scalingCanvas.Width = (outer.Width - ScaleBorder) / config.InterfaceScale;
            scalingCanvas.Height = (outer.Height - ScaleBorder) / config.InterfaceScale;
        }

        bool BeginCornerScaling(Size outer) {
            if (collapsed || !FinishInline(false)) return false;
            CancelItemReordering();
            // At fractional display DPI the one-DIP glass edge can occupy a
            // different physical pixel width. Freeze the arranged canvas and
            // measure those insets instead of remeasuring text on mouse-down.
            cornerInsets = new Size(Math.Max(0, outer.Width - scalingViewport.ActualWidth),
                Math.Max(0, outer.Height - scalingViewport.ActualHeight));
            cornerStartSize = outer; cornerStartScale = config.InterfaceScale;
            cornerScaling = true;
            // Edge resizing must leave controls usable at an enlarged scale.
            // Corners can shrink that scale, so their physical limits are lower.
            MinWidth = 340; MinHeight = 280;
            return true;
        }

        Size ResizeCornerScaling(Size proposed) {
            double width = scalingCanvas.Width, height = scalingCanvas.Height;
            // Project the pointer onto the original diagonal instead of
            // switching between width and height on successive mouse moves.
            double scale = ((proposed.Width - cornerInsets.Width) * width +
                (proposed.Height - cornerInsets.Height) * height) / (width * width + height * height);
            double minimum = Math.Max(0.65, Math.Max((MinWidth - cornerInsets.Width) / width, (MinHeight - cornerInsets.Height) / height));
            double maximum = Math.Max(minimum, Math.Min(1.8,
                Math.Min((MaxWidth - cornerInsets.Width) / width, (MaxHeight - cornerInsets.Height) / height)));
            config.InterfaceScale = Math.Max(minimum, Math.Min(maximum, scale));
            return new Size(cornerInsets.Width + width * config.InterfaceScale, cornerInsets.Height + height * config.InterfaceScale);
        }

        void EndCornerScaling(Size actual) {
            if (!cornerScaling) return;
            // WM_EXITSIZEMOVE also follows Escape. The actual native rectangle
            // is authoritative, including a cancelled gesture's original size.
            scalingHost.UpdateLayout();
            bool restored = Math.Abs(actual.Width - cornerStartSize.Width) < 0.001 &&
                Math.Abs(actual.Height - cornerStartSize.Height) < 0.001;
            config.InterfaceScale = restored ? cornerStartScale : Settings.ClampInterfaceScale(
                scalingCanvas.TransformToAncestor(scalingHost).TransformBounds(new Rect(0, 0, 1, 1)).Width);
            cornerScaling = false;
            ApplyScaledMinimum();
            SaveSettings();
        }
    }
}
