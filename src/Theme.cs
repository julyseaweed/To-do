using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Data;
using System.ComponentModel;
using System.Collections.Generic;

namespace Shike {
    public static class Theme {
        sealed class ColorToken : INotifyPropertyChanged {
            Color value;
            public Color Value { get { return value; } set { this.value = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); } }
            public event PropertyChangedEventHandler PropertyChanged;
        }
        static readonly Dictionary<SolidColorBrush, ColorToken> Tokens = new Dictionary<SolidColorBrush, ColorToken>();
        public static bool Dark = true;
        public static readonly SolidColorBrush Ink = Paint("#F7F8FA"), Line = Paint("#20FFFFFF");
        public static readonly SolidColorBrush Hover = Paint("#18FFFFFF"), Pressed = Paint("#2AFFFFFF"), Ring = Paint("#C8E2E5ED");
        public static readonly SolidColorBrush Panel = Paint("#EE25272D"), Error = Paint("#FFC1B5");
        public static readonly SolidColorBrush CardInk = new SolidColorBrush(Color.FromRgb(9,12,13));
        public static readonly SolidColorBrush CardMuted = new SolidColorBrush(Color.FromRgb(36,43,46));
        static readonly SolidColorBrush WineInk = new SolidColorBrush(Color.FromRgb(255,247,245));
        static readonly SolidColorBrush WineMuted = new SolidColorBrush(Color.FromRgb(255,243,241));
        static ControlTemplate plainTextActionTemplate;
        static Style roundFocusCue, squareFocusCue;
        public static Style FocusCue(bool circular) {
            var style = circular ? roundFocusCue : squareFocusCue;
            if (style != null) return style;
            string shape = circular ? "<Ellipse Margin='2' Stroke='{DynamicResource Ring}' StrokeThickness='1'/>" : "<Border Margin='2' CornerRadius='4' BorderBrush='{DynamicResource Ring}' BorderThickness='1'/>";
            style = (Style)XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Control'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Control'>" + shape + "</ControlTemplate></Setter.Value></Setter></Style>");
            if (circular) roundFocusCue = style; else squareFocusCue = style;
            return style;
        }
        // Left-to-right rainbow; the same hue continues down each column.
        static readonly string[] ReferenceColors = { "#800020", "#EB7900", "#E0C000", "#10AB48", "#00ABB8", "#3885E8", "#9C5DFF" };
        static readonly string[] LightColors = { "#B33A53", "#FFCC91", "#FFED96", "#94E1AB", "#8CE5EA", "#AACBFA", "#CAAEFA" };
        public static Brush CardForeground(int index) { return index % ReferenceColors.Length == 0 ? WineInk : CardInk; }
        public static Brush CardSecondary(int index) { return index % ReferenceColors.Length == 0 ? WineMuted : CardMuted; }
        public static Brush GlassSurface(double transparency, bool blurAvailable) {
            double opacity = 1 - Settings.ClampTransparency(transparency) / 100.0;
            // Keep text readable if the graphics compositor is unavailable.
            if (!blurAvailable) opacity = Math.Max(opacity, 0.80);
            byte alpha = (byte)Math.Round(255 * opacity);
            var brush = new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Color.FromArgb((byte)Math.Min(255,alpha+10),34,37,44),0),
                new GradientStop(Color.FromArgb(alpha,20,22,28),0.35),
                new GradientStop(Color.FromArgb((byte)Math.Min(255,alpha+5),24,26,33),1)
            },new Point(0,0),new Point(0.35,1));
            brush.Freeze(); return brush;
        }
        public static Brush GlassGrain() {
            var pixels = new byte[64*64*4]; var random = new Random(2718);
            for (int i=0; i<pixels.Length; i+=4) {
                byte alpha = (byte)random.Next(0,5);
                // Premultiplied white; at most 1.6% opacity, behind all text.
                pixels[i]=pixels[i+1]=pixels[i+2]=pixels[i+3]=alpha;
            }
            var image = System.Windows.Media.Imaging.BitmapSource.Create(64,64,96,96,PixelFormats.Pbgra32,null,pixels,256);
            image.Freeze();
            var brush = new ImageBrush(image) { TileMode=TileMode.Tile, ViewportUnits=BrushMappingMode.Absolute, Viewport=new Rect(0,0,64,64), Stretch=Stretch.Fill };
            brush.Freeze(); return brush;
        }
        public static Brush CardTint(int index, int row) {
            var color = (Color)ColorConverter.ConvertFromString(ReferenceColors[index % ReferenceColors.Length]);
            var light = (Color)ColorConverter.ConvertFromString(LightColors[index % LightColors.Length]);
            // Position, rather than item count or editing state, determines
            // the shade. Adjacent cards continue the same gentle gradient.
            var brush = new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(ColumnShade(color, light, row), 0),
                new GradientStop(ColumnShade(color, light, row + 1), 1)
            }, new Point(0,0), new Point(0,1));
            brush.Freeze(); return brush;
        }
        static Color ColumnShade(Color color, Color light, double depth) {
            // Interpolate toward a lighter shade of the actual hue, not white.
            // Red therefore stays red instead of becoming a pink pastel.
            double blend = 1 - Math.Exp(-Math.Max(0, depth) / 3.0);
            return Color.FromArgb(235,
                (byte)Math.Round(color.R + (light.R - color.R) * blend),
                (byte)Math.Round(color.G + (light.G - color.G) * blend),
                (byte)Math.Round(color.B + (light.B - color.B) * blend));
        }
        public static Border CardDivider() {
            return new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(42, 255, 240, 240)), Margin = new Thickness(0, 5, 0, 4), IsHitTestVisible = false };
        }
        public static void PlainTextAction(Button button) {
            button.FocusVisualStyle = null; button.ToolTip = null;
            button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0); button.Cursor = System.Windows.Input.Cursors.IBeam;
            if (plainTextActionTemplate == null)
                plainTextActionTemplate = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border></ControlTemplate>");
            button.Template = plainTextActionTemplate;
        }
        public static Brush ColumnDivider() {
            var brush = new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(82, 255, 255, 255), 0.10),
                new GradientStop(Color.FromArgb(42, 255, 255, 255), 0.85),
                new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
            }, new Point(0, 0), new Point(0, 1));
            brush.Freeze(); return brush;
        }
        public static SolidColorBrush Paint(string color) {
            var token = new ColorToken { Value = (Color)ColorConverter.ConvertFromString(color) }; var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding("Value") { Source = token }); Tokens.Add(brush, token); return brush;
        }
        static void Set(SolidColorBrush brush, string dark, string light) { Tokens[brush].Value = (Color)ColorConverter.ConvertFromString(Dark ? dark : light); }
        public static void SetDark(bool dark) {
            if (Dark == dark) return; Dark = dark;
            Set(Ink, "#F7F8FA", "#202127");
            Set(Line, "#20FFFFFF", "#20202630"); Set(Hover, "#18FFFFFF", "#14202630"); Set(Pressed, "#2AFFFFFF", "#25202630");
            Set(Ring, "#C8E2E5ED", "#AE424752"); Set(Panel, "#EE25272D", "#EEF4F5F8"); Set(Error, "#FFC1B5", "#A33D36");
        }
        public static Brush Edge() {
            return new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Color.FromArgb(170,255,255,255),0), new GradientStop(Color.FromArgb(32,255,255,255),0.4),
                new GradientStop(Color.FromArgb(18,255,255,255),0.65), new GradientStop(Color.FromArgb(102,255,255,255),1)
            }, new Point(0,0), new Point(0.85,1));
        }
        public static TextBlock Text(string value, double size, Brush color) {
            return new TextBlock { Text = value, FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        }
        public static Button Button(string text, string help, Action action) {
            var b = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center, MinWidth = 30, MinHeight = 30 };
            AutomationProperties.SetName(b, help); if (action != null) b.Click += delegate { action(); }; return b;
        }
        public static Button Icon(string glyph, string help, Action action) {
            var b = Button(glyph, help, action); b.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"); b.FontSize = 12; b.Width = 32; return b;
        }
        public static ResourceDictionary Resources() {
            var d = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Button'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/>
  <Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='7,4'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='12'/>
  <Setter Property='FocusVisualStyle' Value='{DynamicResource KeyboardFocusCue}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6' Padding='{TemplateBinding Padding}'>
    <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Background' Value='{DynamicResource Pressed}'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='CheckBox'>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='Width' Value='30'/><Setter Property='MinHeight' Value='34'/><Setter Property='VerticalAlignment' Value='Center'/>
  <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
   <Grid Background='Transparent' UseLayoutRounding='False' SnapsToDevicePixels='False'>
    <Ellipse x:Name='RingShape' Width='16' Height='16' Stroke='{DynamicResource Ring}' StrokeThickness='1.2' Fill='Transparent' HorizontalAlignment='Center' VerticalAlignment='Center'/>
    <Ellipse x:Name='CompletionDot' Width='9' Height='9' Fill='{DynamicResource Ink}' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed' IsHitTestVisible='False'/>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='RingShape' Property='Stroke' Value='{DynamicResource Ink}'/><Setter TargetName='CompletionDot' Property='Visibility' Value='Visible'/></Trigger>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='RingShape' Property='Stroke' Value='{DynamicResource Ink}'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='RingShape' Property='Stroke' Value='{DynamicResource Ink}'/><Setter TargetName='RingShape' Property='StrokeThickness' Value='2'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='SlimScrollBar' TargetType='ScrollBar'>
  <Setter Property='Width' Value='5'/><Setter Property='MinWidth' Value='0'/><Setter Property='MaxWidth' Value='5'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='2' Background='{DynamicResource Ring}' Opacity='0.45' Margin='1,2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ListScrollViewer' TargetType='ScrollViewer'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'>
   <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
    <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}'/>
    <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Style='{StaticResource SlimScrollBar}' Orientation='Vertical' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}'/>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ScrollTrackPageButton' TargetType='RepeatButton'>
  <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='SlimHorizontalScrollBar' TargetType='ScrollBar'>
  <Setter Property='Height' Value='12'/><Setter Property='MinHeight' Value='0'/><Setter Property='MaxHeight' Value='12'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Track x:Name='PART_Track' Orientation='Horizontal' IsDirectionReversed='False'>
    <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageLeftCommand}' Style='{StaticResource ScrollTrackPageButton}'/></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'><Border Height='3' CornerRadius='1.5' Background='{DynamicResource Ring}' Opacity='0.45' Margin='2,0' VerticalAlignment='Center'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageRightCommand}' Style='{StaticResource ScrollTrackPageButton}'/></Track.IncreaseRepeatButton>
   </Track>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='TopicStripScrollViewer' TargetType='ScrollViewer'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollViewer'>
   <Grid><Grid.RowDefinitions><RowDefinition/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}'/>
    <ScrollBar x:Name='PART_HorizontalScrollBar' Grid.Row='1' Style='{StaticResource SlimHorizontalScrollBar}' Orientation='Horizontal' Visibility='{TemplateBinding ComputedHorizontalScrollBarVisibility}' Maximum='{TemplateBinding ScrollableWidth}' ViewportSize='{TemplateBinding ViewportWidth}' Value='{Binding HorizontalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}'/>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ToolTip'><Setter Property='Background' Value='{DynamicResource Panel}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='Padding' Value='9,6'/></Style>
 <Style TargetType='ContextMenu'>
  <Setter Property='Background' Value='{DynamicResource Panel}'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='Padding' Value='6'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'>
   <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='{TemplateBinding Padding}'>
    <ScrollViewer CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'><StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/></ScrollViewer>
   </Border>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='MenuItem'>
  <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='Transparent'/><Setter Property='Padding' Value='10,7'/><Setter Property='FontSize' Value='12'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
   <Border x:Name='MenuSurface' Background='{TemplateBinding Background}' CornerRadius='5' Padding='{TemplateBinding Padding}'>
    <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='20'/></Grid.ColumnDefinitions>
     <ContentPresenter ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
     <Ellipse x:Name='CheckedDot' Grid.Column='1' Width='6' Height='6' Fill='{TemplateBinding Foreground}' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>
    </Grid>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='MenuSurface' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
    <Trigger Property='IsChecked' Value='True'><Setter TargetName='CheckedDot' Property='Visibility' Value='Visible'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='MenuControlItem' TargetType='MenuItem' BasedOn='{StaticResource {x:Type MenuItem}}'>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'><Border Padding='{TemplateBinding Padding}' Background='Transparent'><ContentPresenter ContentSource='Header'/></Border></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='Separator'>
  <Setter Property='Margin' Value='0'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Background='{DynamicResource Line}' Margin='10,5'/></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='SliderTrackButton' TargetType='RepeatButton'>
  <Setter Property='Focusable' Value='False'/><Setter Property='IsTabStop' Value='False'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Grid Background='Transparent'><Border Height='3' CornerRadius='1.5' Background='{TemplateBinding Background}' VerticalAlignment='Center'/></Grid></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='TransparencySlider' TargetType='Slider'>
  <Setter Property='Height' Value='28'/><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='FocusVisualStyle' Value='{DynamicResource KeyboardFocusCue}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
   <Grid Background='Transparent'>
    <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' Orientation='Horizontal' IsDirectionReversed='False'>
     <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Style='{StaticResource SliderTrackButton}' Background='{DynamicResource Ring}'/></Track.DecreaseRepeatButton>
     <Track.Thumb><Thumb Width='14' Height='14' VerticalAlignment='Center' Cursor='Hand'>
      <Thumb.Template><ControlTemplate TargetType='Thumb'><Ellipse Fill='{DynamicResource Ink}'/></ControlTemplate></Thumb.Template>
     </Thumb></Track.Thumb>
     <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Style='{StaticResource SliderTrackButton}' Background='{DynamicResource Line}'/></Track.IncreaseRepeatButton>
    </Track>
   </Grid>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>");
            d["Ink"] = Ink; d["Line"] = Line; d["Hover"] = Hover; d["Pressed"] = Pressed;
            d["Ring"] = Ring; d["Panel"] = Panel; d["KeyboardFocusCue"] = FocusCue(false); return d;
        }
    }
}
