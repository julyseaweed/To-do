using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Shike {
    public sealed partial class MainWindow {
        // Display and editing share WPF's text layout, including glyph inset,
        // baseline and wrapping. The surrounding button owns display actions.
        sealed class DisplayTextField : TextBox {
            protected override AutomationPeer OnCreateAutomationPeer() { return null; }
        }
        static ControlTemplate textFieldTemplate;
        enum TextFieldRole { Heading, Body, Link }
        static readonly FontFamily bodyTextFont = CreateBodyTextFont();
        int? clickedTextCaret;

        static FontFamily CreateBodyTextFont() {
            // Keep Segoe's Latin and YaHei's CJK glyphs, but give their shared
            // line box a baseline that optically centers text beside the circle.
            // Preserve Segoe's generous line spacing for accents and descenders.
            var family = new FontFamily { Baseline = 1.0, LineSpacing = 1.33 };
            family.FamilyMaps.Add(new FontFamilyMap { Target = "Segoe UI, Microsoft YaHei UI" });
            return family;
        }

        TextBox TextField(string value, double size, Brush ink, TextFieldRole role, bool display) {
            if (textFieldTemplate == null)
                textFieldTemplate = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><ScrollViewer x:Name='PART_ContentHost'/></ControlTemplate>");
            TextBox input = display ? new DisplayTextField() : new TextBox();
            input.Text = value ?? "";
            input.FontFamily = role == TextFieldRole.Heading ? FontFamily : bodyTextFont; input.FontSize = size;
            input.FontWeight = role == TextFieldRole.Heading ? FontWeights.Bold : FontWeights.Normal;
            input.Foreground = ink; input.CaretBrush = ink;
            input.Background = Brushes.Transparent; input.BorderThickness = new Thickness(0);
            input.Padding = new Thickness(0); input.FocusVisualStyle = null;
            input.MinHeight = size + 7;
            input.VerticalContentAlignment = VerticalAlignment.Center;
            input.TextWrapping = TextWrapping.Wrap;
            input.AcceptsReturn = false; input.AcceptsTab = false;
            input.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            input.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            input.Template = textFieldTemplate;
            if (display) {
                input.IsReadOnly = true; input.IsReadOnlyCaretVisible = false;
                input.IsUndoEnabled = false;
                input.Focusable = false; input.IsTabStop = false; input.IsHitTestVisible = false;
            }
            return input;
        }

        static int CaretFromPoint(TextBox field, Point point) {
            int index = field.GetCharacterIndexFromPoint(point, true);
            if (index < 0) return 0;
            if (index >= field.Text.Length) return field.Text.Length;
            // WPF returns the character, not its insertion edge. Preserve
            // right-half clicks, including the last glyph and RTL text.
            Rect leading = field.GetRectFromCharacterIndex(index, false);
            Rect trailing = field.GetRectFromCharacterIndex(index, true);
            if (!leading.IsEmpty && !trailing.IsEmpty) {
                double before = Math.Pow(point.X - leading.X, 2) + Math.Pow(point.Y - leading.Top - leading.Height / 2, 2);
                double after = Math.Pow(point.X - trailing.X, 2) + Math.Pow(point.Y - trailing.Top - trailing.Height / 2, 2);
                if (after < before) index++;
            }
            return index;
        }

        Button TextFieldAction(TextBox field, string name, System.Action action) {
            int? pointerCaret = null;
            var button = Theme.Button("", name, delegate {
                clickedTextCaret = pointerCaret; pointerCaret = null;
                try { action(); } finally { clickedTextCaret = null; }
            });
            button.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                pointerCaret = CaretFromPoint(field, e.GetPosition(field));
            };
            button.PreviewMouseLeftButtonUp += delegate { if (!button.IsMouseOver) pointerCaret = null; };
            button.LostMouseCapture += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) pointerCaret = null; };
            button.PreviewKeyDown += delegate { pointerCaret = null; };
            Theme.PlainTextAction(button);
            button.Content = field; button.Padding = new Thickness(0);
            button.MinHeight = 0; button.MinWidth = 0;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            return button;
        }

        static StackPanel ItemTextPanel() {
            return new StackPanel { Margin = new Thickness(2, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        }

        static void AlignItemCheck(CheckBox check, TextBox title, FrameworkElement textPanel, Grid row) {
            check.VerticalAlignment = VerticalAlignment.Top;
            var offset = new TranslateTransform(); check.RenderTransform = offset;
            bool pending = false;
            Action align = delegate {
                if (title.ActualHeight <= 0 || check.ActualHeight <= 0 || !title.IsArrangeValid) return;
                Rect line = title.GetRectFromCharacterIndex(0);
                if (line.IsEmpty) return;
                double center = title.TranslatePoint(new Point(0, line.Top + line.Height / 2), row).Y;
                double top = center - check.ActualHeight / 2;
                // Moving only the checkbox keeps the card's measured geometry
                // unchanged and avoids rounding negative margins on wrapped rows.
                if (Math.Abs(offset.Y - top) > .01) offset.Y = top;
            };
            Action schedule = delegate {
                // An arranged text view already has its first line. The queued
                // pass also covers initial template creation and parent moves.
                align();
                if (pending) return;
                pending = true;
                title.Dispatcher.BeginInvoke(new Action(delegate { pending = false; align(); }), DispatcherPriority.Loaded);
            };
            title.Loaded += delegate { schedule(); };
            title.SizeChanged += delegate { schedule(); };
            title.TextChanged += delegate { schedule(); };
            textPanel.SizeChanged += delegate { schedule(); };
            row.SizeChanged += delegate { schedule(); };
            check.SizeChanged += delegate { schedule(); };
        }
    }
}
