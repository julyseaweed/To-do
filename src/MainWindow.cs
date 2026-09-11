using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Shike {
    public sealed partial class MainWindow : Window {
        public readonly NoteStore Store;
        Settings config;
        string stateDirectory;
        NoteDocument document;
        StackPanel toastContent, columns;
        Grid groups;
        Border watchingColumn, columnDivider;
        readonly Dictionary<string, ScrollViewer> columnScrolls = new Dictionary<string, ScrollViewer>();
        readonly Dictionary<string, double> columnOffsets = new Dictionary<string, double>();
        Grid layout, windowRoot, bubble, footerGrid;
        ScrollViewer scroll;
        Border surface, footer, toast;
        Grid header;
        TextBlock appTitle;
        Button collapse, more, close;
        DispatcherTimer watcher, toastTimer, contrastTimer;
        bool collapsed, resident, exiting, blurAvailable;
        double expandedHeight, expandedWidth, expandedLeft, expandedTop;
        EventHandler dockAnimation;
        System.Windows.Forms.NotifyIcon tray;
        string undoBefore, undoAfter;
        public MainWindow(NoteStore store, Settings settings, string stateDir, bool stayResident = false) {
            Store = store; config = settings; stateDirectory = stateDir; resident = stayResident; Title = "To-do";
            config.Transparency = Settings.ClampTransparency(config.Transparency);
            config.InterfaceScale = Settings.ClampInterfaceScale(config.InterfaceScale);
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Foreground = Theme.Ink;
            FontSize = 13; UseLayoutRounding = true; SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Resources = Theme.Resources(); ResizeMode = ResizeMode.CanResize;
            var work = SystemParameters.WorkArea;
            ApplyScaledMinimum(); Width = Math.Max(MinWidth, Math.Min(work.Width - 48, config.Width));
            Height = Math.Max(MinHeight, Math.Min(work.Height - 32, config.Height));
            Left = config.Left < 0 ? work.Left + (work.Width - Width) / 2 : Math.Max(work.Left, Math.Min(work.Right - Width, config.Left));
            Top = config.Top < 0 ? work.Top + Math.Max(16, (work.Height - Height) / 2) : Math.Max(work.Top, Math.Min(work.Bottom - Height, config.Top));
            Topmost = config.Pinned; ShowInTaskbar = !resident; Desktop.Chrome(this);
            using (var icon = Desktop.MakeIcon()) Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Build();
            EnableItemReordering();
            SourceInitialized += delegate {
                blurAvailable = Desktop.Glass(this); ApplyGlass();
                Desktop.EnableCornerResize(this, BeginCornerScaling, ResizeCornerScaling, EndCornerScaling, SaveSettings);
                EnableHorizontalScrolling();
            };
            PreviewKeyDown += OnKey;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (!FinishInline(false)) { e.Cancel = true; exiting = false; return; }
                if (resident && !exiting) { e.Cancel = true; HidePanel(); return; }
                watcher.Stop(); toastTimer.Stop(); contrastTimer.Stop();
                StopDockAnimation();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                SaveSettings();
            };
            Deactivated += delegate { FinishInline(false); SaveSettings(); };
            watcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            watcher.Tick += delegate {
                if (inline != null || IsItemReordering || watchingResizeActive || Desktop.IsInteracting(this) || dockAnimation != null) return;
                try { var fresh = Store.Load(); if (document == null || fresh.Text != document.Text) { document = fresh; Render(); } }
                catch (IOException) { /* Retry while Obsidian replaces the file. */ }
                catch (UnauthorizedAccessException) { Notify("Cannot read the list. Check the folder permissions.", false, true); }
            };
            watcher.Start();
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            toastTimer.Tick += delegate { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
            contrastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            contrastTimer.Tick += delegate {
                if (inline != null || IsItemReordering || watchingResizeActive || collapsed || dockAnimation != null || Desktop.IsInteracting(this)) return;
                var light = Desktop.GlassLuminance(this);
                if (light.HasValue) { if (light.Value > 0.62) Theme.SetDark(false); else if (light.Value < 0.48) Theme.SetDark(true); }
            };
            contrastTimer.Start();
            if (resident) {
                tray = new System.Windows.Forms.NotifyIcon { Icon = Desktop.MakeIcon(), Text = "To-do", Visible = true };
                var trayMenu = new System.Windows.Forms.ContextMenuStrip();
                trayMenu.Items.Add("Open list", null, delegate { Dispatcher.Invoke(new Action(Restore)); });
                trayMenu.Items.Add("Collapse", null, delegate { Dispatcher.Invoke(new Action(delegate { Show(); if (!collapsed) ToggleCollapsed(); })); });
                trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                trayMenu.Items.Add("Quit", null, delegate { Dispatcher.Invoke(new Action(Exit)); });
                tray.ContextMenuStrip = trayMenu; tray.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(new Action(Restore)); };
                Application.Current.SessionEnding += delegate { exiting = true; SaveSettings(); };
            }
            Refresh();
        }
        public void Exit() { exiting = true; Close(); }
        public void HidePanel() { CancelItemReordering(); if (!FinishInline(false)) return; StopDockAnimation(); SaveSettings(); Hide(); }
        public void Restore() {
            if (!FinishInline(false)) return;
            StopDockAnimation();
            if (collapsed && IsVisible) Hide();
            WindowState = WindowState.Normal;
            if (collapsed) ToggleCollapsed();
            config.ApplyLaunchLayout(); ApplyScaledMinimum();
            var work = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(work.Width - 48, config.Width));
            Height = Math.Max(MinHeight, Math.Min(work.Height - 32, config.Height));
            Left = config.Left < 0 ? work.Left + (work.Width - Width) / 2 : Math.Max(work.Left, Math.Min(work.Right - Width, config.Left));
            Top = config.Top < 0 ? work.Top + Math.Max(16, (work.Height - Height) / 2) : Math.Max(work.Top, Math.Min(work.Bottom - Height, config.Top));
            UpdateScalingViewport(new Size(Width, Height));
            // Hidden panels must reach their launch bounds before either the
            // content window or its glass background becomes visible again.
            UpdateLayout(); Show(); Activate(); SaveSettings();
        }
        void RestoreFromBubble() {
            if (!collapsed) { Show(); WindowState = WindowState.Normal; Activate(); return; }
            StopDockAnimation();
            var position = new Rect(Left, Top, Width, Height);
            var work = Desktop.WorkArea(this);
            bool rightHalf = position.Left + position.Width / 2 >= work.Left + work.Width / 2;
            Show(); ToggleCollapsed();
            if (collapsed) return;
            double left = rightHalf ? position.Right - Width : position.Left;
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, position.Top));
            WindowState = WindowState.Normal; Activate(); SaveSettings();
        }
        void SaveSettings() {
            if (WindowState == WindowState.Normal) { config.Left = collapsed ? expandedLeft : Left; config.Top = collapsed ? expandedTop : Top; config.Width = collapsed ? expandedWidth : Width; config.Height = collapsed ? expandedHeight : Height; }
            // Pinned belongs to the expanded panel. The bubble's temporary
            // always-on-top layer must never become a saved panel preference.
            try { config.Save(stateDirectory); } catch { }
        }
        void ApplyGlass() {
            surface.Background = Theme.GlassSurface(config.Transparency, blurAvailable);
        }
        void Build() {
            layout = new Grid { Margin = new Thickness(1) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var glassLayers = new Grid();
            glassLayers.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = Theme.GlassGrain(), IsHitTestVisible = false });
            glassLayers.Children.Add(BuildScalingHost(glassLayers));
            surface = new Border { CornerRadius = new CornerRadius(12), BorderBrush = Theme.Edge(), BorderThickness = new Thickness(1), Child = glassLayers };
            ApplyGlass();
            windowRoot = new Grid(); windowRoot.Children.Add(surface); Content = windowRoot;
            header = new Grid { Height = 44, Margin = new Thickness(24, 0, 14, 0), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var artwork = new BitmapImage();
            using (var source = typeof(MainWindow).Assembly.GetManifestResourceStream("Shike.Artwork")) {
                artwork.BeginInit(); artwork.CacheOption = BitmapCacheOption.OnLoad; artwork.StreamSource = source; artwork.EndInit(); artwork.Freeze();
            }
            appTitle = Theme.Text("To-do", 24, Theme.Ink); appTitle.FontWeight = FontWeights.Bold; appTitle.FontFamily = new FontFamily("Segoe UI");
            appTitle.RenderTransform = new TranslateTransform(0, 5);
            brand.Children.Add(appTitle); header.Children.Add(brand);
            BuildBubble(artwork);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            collapse = HeaderButton("Collapse / Expand", ToggleCollapsed, false);
            more = HeaderButton("More", null, true); more.Click += delegate { var menu = MoreMenu(); menu.PlacementTarget = more; menu.IsOpen = true; };
            close = CloseButton("Close panel", delegate { if (resident) HidePanel(); else Close(); }, false);
            controls.Children.Add(more); controls.Children.Add(collapse); controls.Children.Add(close); Grid.SetColumn(controls, 1); header.Children.Add(controls);
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } }
            };
            layout.Children.Add(header);
            groups = new Grid { Margin = new Thickness(18, 2, 18, 0) };
            groups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            groups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            groups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            groups.SizeChanged += delegate { UpdateColumns(); };
            scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, PanningMode = PanningMode.HorizontalOnly };
            scroll.Style = (Style)Resources["TopicStripScrollViewer"];
            AutomationProperties.SetName(scroll, "Topics");
            Grid.SetColumn(scroll, 2); groups.Children.Add(scroll);
            BuildWatchingDivider();
            Grid.SetRow(groups, 1); layout.Children.Add(groups);
            scroll.SizeChanged += delegate { UpdateColumns(); };
            groups.LayoutTransform = new ScaleTransform(config.Zoom, config.Zoom);
            footerGrid = new Grid { Margin = new Thickness(18, 9, 18, 9) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var add = Theme.Button("＋  New topic", "New topic", delegate { EditGroup(null); });
            add.HorizontalAlignment = HorizontalAlignment.Left; add.Foreground = Theme.Ink; add.Padding = new Thickness(13, 6, 15, 6); add.Background = Theme.Hover; Grid.SetColumn(add, 1); footerGrid.Children.Add(add);
            footer = new Border { Child = footerGrid };
            Grid.SetRow(footer, 2); layout.Children.Add(footer);
            toastContent = new StackPanel { Orientation = Orientation.Horizontal };
            toast = new Border { CornerRadius = new CornerRadius(6), Background = Theme.Panel, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(12, 0, 12, 8), VerticalAlignment = VerticalAlignment.Bottom, Child = toastContent, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetRow(toast, 1); Panel.SetZIndex(toast, 2); layout.Children.Add(toast);
        }
        void ToggleCollapsed() {
            if (!FinishInline(false)) return;
            StopDockAnimation();
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            if (!collapsed) {
                expandedHeight = Height; expandedWidth = Width; expandedLeft = Left; expandedTop = Top;
                var work = Desktop.WorkArea(this);
                var start = new Point(expandedLeft + expandedWidth - 64, expandedTop + 8);
                var target = new Point(work.Right - 64 - 12, Math.Max(work.Top+12,Math.Min(work.Bottom-64-12,start.Y)));
                collapsed = true; Topmost = true;
                surface.Visibility = Visibility.Collapsed; bubble.Visibility = Visibility.Visible;
                MinHeight = MinWidth = 64; ResizeMode = ResizeMode.NoResize; Width = Height = 64;
                Desktop.Move(this,start); Desktop.ClipBubble(this,true); MoveToEdge(target);
                bubble.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));
            } else {
                collapsed = false; Topmost = config.Pinned;
                bubble.Visibility = Visibility.Collapsed; Desktop.ClipBubble(this,false);
                ApplyScaledMinimum(); ResizeMode = ResizeMode.CanResize;
                Width = Math.Max(MinWidth,expandedWidth); Height = Math.Max(MinHeight,expandedHeight);
                Left = expandedLeft; Top = expandedTop;
                surface.Visibility = Visibility.Visible;
                surface.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));
            }
            SaveSettings();
        }
        void BuildBubble(ImageSource artwork) {
            bubble = new Grid { Width = 64, Height = 64, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var open = Theme.Button("", "Open list", RestoreFromBubble); open.Width = open.Height = 56;
            open.FocusVisualStyle = Theme.FocusCue(true);
            open.HorizontalAlignment = HorizontalAlignment.Left; open.VerticalAlignment = VerticalAlignment.Bottom;
            open.Padding = new Thickness(0); open.Background = new SolidColorBrush(Color.FromArgb(60,242,232,235));
            open.BorderBrush = Theme.Edge(); open.BorderThickness = new Thickness(1);
            open.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='28'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
            open.Content = new Image { Source = artwork, Width = 27, Height = 38, Stretch = Stretch.Uniform };
            Point pointerStart = new Point(); bool dragged = false;
            open.PreviewMouseLeftButtonDown += delegate(object sender,MouseButtonEventArgs e) { StopDockAnimation(); pointerStart = PointToScreen(e.GetPosition(this)); dragged = false; };
            open.PreviewMouseMove += delegate(object sender,MouseEventArgs e) {
                if (e.LeftButton != MouseButtonState.Pressed || dragged) return;
                var position = PointToScreen(e.GetPosition(this));
                if ((position-pointerStart).Length < 5) return;
                dragged = true; open.ReleaseMouseCapture();
                try { DragMove(); } catch (InvalidOperationException) { }
                e.Handled = true;
            };
            open.PreviewMouseLeftButtonUp += delegate(object sender,MouseButtonEventArgs e) { if (dragged) e.Handled = true; };
            var dismiss = CloseButton("Close bubble", delegate { if (resident) HidePanel(); else Close(); }, true);
            dismiss.HorizontalAlignment = HorizontalAlignment.Right; dismiss.VerticalAlignment = VerticalAlignment.Top;
            bubble.Children.Add(open); bubble.Children.Add(dismiss); windowRoot.Children.Add(bubble);
        }
        static Button CloseButton(string help, Action action, bool circular) {
            string glyphSize = circular ? "8" : "10";
            string glyph = "<Path x:Name='CloseGlyph' Data='M 0,0 L 8,8 M 8,0 L 0,8' Width='" + glyphSize + "' Height='" + glyphSize + "' Stretch='Uniform' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.35' StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'/>";
            return WindowControlButton(help, action, circular, glyph);
        }
        static Button HeaderButton(string help, Action action, bool ellipsis) {
            // Both symbols share the same 12-DIP canvas and geometric center.
            // A short line reads as collapse without relying on a font baseline.
            string glyph = "<Canvas x:Name='HeaderGlyph' Width='12' Height='12' HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'>" +
                (ellipsis
                    ? "<Ellipse Canvas.Left='0' Canvas.Top='5' Width='2' Height='2' Fill='{TemplateBinding Foreground}'/><Ellipse Canvas.Left='5' Canvas.Top='5' Width='2' Height='2' Fill='{TemplateBinding Foreground}'/><Ellipse Canvas.Left='10' Canvas.Top='5' Width='2' Height='2' Fill='{TemplateBinding Foreground}'/>"
                    : "<Line X1='1' Y1='6' X2='11' Y2='6' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.35' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>") + "</Canvas>";
            return WindowControlButton(help, action, false, glyph);
        }
        static Button WindowControlButton(string help, Action action, bool circular, string glyph) {
            var button = Theme.Button("", help, action);
            button.Width = button.MinWidth = circular ? 20 : 32;
            button.Height = button.MinHeight = circular ? 20 : 30;
            button.Padding = new Thickness(0); button.Foreground = Theme.Ink;
            // Keep the two diagonals symmetric on fractional display scales;
            // rounding each inner edge separately can shift the glyph by a pixel.
            button.UseLayoutRounding = false; button.SnapsToDevicePixels = false;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.FocusVisualStyle = Theme.FocusCue(circular);
            button.Background = circular ? Theme.Panel : Brushes.Transparent;
            button.BorderBrush = circular ? Theme.Edge() : Brushes.Transparent;
            // Vector controls share centered hit areas and feedback surfaces.
            string surface = circular
                ? "<Ellipse Fill='{TemplateBinding Background}' Stroke='{TemplateBinding BorderBrush}' StrokeThickness='1'/><Ellipse x:Name='Feedback' Fill='Transparent'/>"
                : "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='6'/><Border x:Name='Feedback' Background='Transparent' CornerRadius='6'/>";
            string fillProperty = circular ? "Fill" : "Background";
            button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Grid>" + surface +
                glyph + "</Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Feedback' Property='" + fillProperty + "' Value='{DynamicResource Hover}'/></Trigger>" +
                "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Feedback' Property='" + fillProperty + "' Value='{DynamicResource Pressed}'/></Trigger>" +
                "<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            return button;
        }
        void StopDockAnimation() { if (dockAnimation != null) { CompositionTarget.Rendering -= dockAnimation; dockAnimation = null; } }
        void MoveToEdge(Point target) {
            StopDockAnimation();
            if (!SystemParameters.ClientAreaAnimation) { Desktop.Move(this,target); return; }
            var start = new Point(Left,Top); var clock = System.Diagnostics.Stopwatch.StartNew();
            dockAnimation = delegate {
                double t = Math.Min(1,clock.Elapsed.TotalMilliseconds/220); double eased = 1-Math.Pow(1-t,3);
                Desktop.Move(this,new Point(start.X+(target.X-start.X)*eased,start.Y+(target.Y-start.Y)*eased));
                if (t >= 1) StopDockAnimation();
            };
            CompositionTarget.Rendering += dockAnimation;
        }
        public void Refresh() { document = Store.Load(); Render(); }
        string GroupLabel(string group) {
            return NoteDocument.IsEmptyGroup(group)
                ? "empty topic " + (document.Groups.Where(x => x != NoteStore.ReadingGroup).ToList().IndexOf(group) + 1)
                : group;
        }
        void Render() {
            CancelItemReordering();
            PrepareCompletionRender();
            double horizontalOffset = scroll.HorizontalOffset;
            foreach (var pair in columnScrolls) columnOffsets[pair.Key] = pair.Value.VerticalOffset;
            columnScrolls.Clear();
            if (watchingColumn != null) groups.Children.Remove(watchingColumn);
            columns = new StackPanel { Orientation = Orientation.Horizontal };
            scroll.Content = columns;
            var visibleGroups = new List<string> { NoteStore.ReadingGroup };
            visibleGroups.AddRange(document.Groups.Where(x => x != NoteStore.ReadingGroup));
            int colorIndex = 0;
            foreach (string name in visibleGroups) {
                // A column keeps its tint through empty, editing and saved
                // states, independently of item counts in any other column.
                int columnColor = colorIndex;
                bool watching = name == NoteStore.ReadingGroup;
                var section = new Grid { Margin = new Thickness(0, 6, 0, 4) };
                section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                section.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var heading = new Grid { MinHeight = 36, Margin = new Thickness(7, 0, 4, 6) };
                heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (inline != null && inline.IsTopic && inline.Group == name) heading.Children.Add(InlineTopic(inline));
                else {
                    string title = watching ? WatchingTitle : NoteDocument.GroupTitle(name);
                    var rename = TextFieldAction(TextField(title, 19, Theme.Ink, TextFieldRole.Heading, true), "Rename " + GroupLabel(name), delegate { EditGroup(name); });
                    rename.Margin = new Thickness(0, 0, 8, 0); heading.Children.Add(rename);
                }
                string capturedGroup = name;
                var topicMenu = new ContextMenu();
                AddMenu(topicMenu, "Rename topic", delegate { EditGroup(capturedGroup); });
                AddMenu(topicMenu, "Delete topic", delegate { DeleteGroup(capturedGroup); });
                if (!watching) heading.ContextMenu = topicMenu;
                var plus = Theme.Icon("\uE710", "Add to " + GroupLabel(name), delegate { Edit(null, capturedGroup); }); plus.FontSize = 11; plus.Foreground = Theme.Ink; plus.BorderBrush = Theme.Line; Grid.SetColumn(plus, 1); heading.Children.Add(plus);
                section.Children.Add(heading);
                var cards = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
                var list = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, PanningMode = PanningMode.VerticalOnly };
                list.Style = (Style)Resources["ListScrollViewer"];
                AutomationProperties.SetName(list, "Items in " + GroupLabel(name));
                // Consume vertical wheel input here even at the ends, so it
                // never moves the next column or the horizontal topic strip.
                list.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && !watching) { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true; return; }
                    if (list.ScrollableHeight > 0) list.ScrollToVerticalOffset(list.VerticalOffset - e.Delta * 0.42);
                    e.Handled = true;
                };
                Grid.SetRow(list, 1); section.Children.Add(list);
                columnScrolls[name] = list;
                double offset;
                if (columnOffsets.TryGetValue(name, out offset)) list.ScrollToVerticalOffset(offset);
                var items = document.Entries.Where(x => x.Group == name && (!x.Done || IsCompletionVisible(x))).ToList();
                bool hasDraft = inline != null && !inline.IsTopic && inline.Group == name;
                int rowIndex = 0;
                foreach (var entry in items) {
                    cards.Children.Add(hasDraft && ReferenceEquals(inline.Original, entry) ? InlineItem(inline, columnColor, rowIndex) : Row(entry, columnColor, rowIndex));
                    rowIndex++;
                }
                if (hasDraft && inline.Original == null) cards.Children.Add(InlineItem(inline, columnColor, rowIndex));
                var column = new Border { Child = section, MinHeight = 120, Margin = new Thickness(0, 0, watching ? 0 : 14, 0) };
                AutomationProperties.SetName(column, "Column " + GroupLabel(name));
                if (watching) { watchingColumn = column; groups.Children.Add(column); }
                else columns.Children.Add(column);
                colorIndex++;
            }
            UpdateColumns();
            scroll.ScrollToHorizontalOffset(horizontalOffset);
        }
        void UpdateColumns() {
            if (columns == null || groups == null) return;
            double available = AvailableColumnWidth;
            int count = columns.Children.Count;
            // Round down before arranging so rounding each child cannot add
            // a spurious horizontal scrollbar to an otherwise fitted strip.
            double width, watchingWidth;
            WatchingColumnWidths(available, count, out watchingWidth, out width);
            groups.ColumnDefinitions[0].Width = new GridLength(watchingWidth);
            // The footer stays outside text zoom while following the topic pane.
            footerGrid.ColumnDefinitions[0].Width = new GridLength((watchingWidth + 26) * config.Zoom);
            for (int i = 0; i < columns.Children.Count; i++) {
                var child = (FrameworkElement)columns.Children[i]; child.Width = width;
                child.Margin = new Thickness(0, 0, i == columns.Children.Count - 1 ? 0 : 14, 0);
            }
        }
        FrameworkElement Row(Entry item, int colorIndex, int rowIndex) {
            bool watching = item.Group == NoteStore.ReadingGroup;
            Brush ink = Theme.CardForeground(colorIndex), muted = Theme.CardSecondary(colorIndex);
            bool blank = string.IsNullOrWhiteSpace(item.Title) && string.IsNullOrWhiteSpace(item.Link) && string.IsNullOrWhiteSpace(item.Note);
            string itemLabel = blank ? "empty item " + (rowIndex + 1) + " in " + GroupLabel(item.Group) : !string.IsNullOrWhiteSpace(item.Title) ? item.Title : !string.IsNullOrWhiteSpace(item.Link) ? item.Link : item.Note;
            var row = new Grid { MinHeight = watching ? 74 : 44, Background = Brushes.Transparent, Margin = new Thickness(7, 0, 5, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            var check = new CheckBox { IsChecked = item.Done };
            AutomationProperties.SetName(check, (blank ? "Remove " : item.Done ? "Restore " : "Complete ") + itemLabel);
            RoutedEventHandler toggle = delegate { if (blank) { Change(item, null, item.Group, "Deleted"); return; } var changed = Copy(item); changed.Done = check.IsChecked == true; Change(item, changed, item.Group, changed.Done ? "Completed" : "Restored"); };
            check.Checked += toggle; check.Unchecked += toggle;
            row.Children.Add(check);
            var content = ItemTextPanel();
            var title = TextField(item.Title, 15.5, ink, TextFieldRole.Body, true);
            var titleButton = TextFieldAction(title, "Edit " + itemLabel, delegate { Edit(item, item.Group); });
            AlignItemCheck(check, title, content, row);
            content.Children.Add(titleButton);
            if (watching) {
                content.Children.Add(Theme.CardDivider());
                var linkButton = TextFieldAction(TextField(item.Link, 11.5, ink, TextFieldRole.Link, true), "Edit link " + itemLabel, delegate { EditLink(item); });
                content.Children.Add(linkButton);
            }
            if (!string.IsNullOrWhiteSpace(item.Note)) { var hint = Theme.Text(item.Note, 11, muted); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(hint); }
            Grid.SetColumn(content, 1); row.Children.Add(content);
            var action = string.IsNullOrWhiteSpace(item.Link) ? Theme.Icon("\uE70F", "Edit " + itemLabel, delegate { Edit(item, item.Group); }) : Theme.Icon("\uE8A7", "Open " + itemLabel, delegate { Open(item.Link); });
            Theme.PlainTextAction(action); action.Cursor = string.IsNullOrWhiteSpace(item.Link) ? Cursors.IBeam : Cursors.Hand;
            action.Width = 27; action.MinWidth = 27; action.FontSize = 11; action.Padding = new Thickness(4); action.Opacity = string.IsNullOrWhiteSpace(item.Link) ? 0 : 0.55;
            Grid.SetColumn(action, 2); row.Children.Add(action);
            row.MouseEnter += delegate { action.Opacity = 0.9; }; row.MouseLeave += delegate { action.Opacity = string.IsNullOrWhiteSpace(item.Link) ? 0 : 0.55; };
            action.GotKeyboardFocus += delegate { action.Opacity = 1; };
            var context = new ContextMenu();
            AddMenu(context, "Edit", delegate { Edit(item, item.Group); });
            AddMenu(context, string.IsNullOrWhiteSpace(item.Link) ? "Add link" : "Edit link", delegate { EditLink(item); });
            if (!string.IsNullOrWhiteSpace(item.Link)) AddMenu(context, "Open link", delegate { Open(item.Link); });
            if (!blank) AddMenu(context, item.Done ? "Restore item" : "Complete", delegate { var changed = Copy(item); changed.Done = !item.Done; Change(item, changed, item.Group, changed.Done ? "Completed" : "Restored"); });
            context.Items.Add(new Separator { Style = (Style)Resources[typeof(Separator)] }); AddMenu(context, "Delete", delegate { Change(item, null, item.Group, "Deleted"); });
            row.ContextMenu = context;
            var pill = new Border { Child = row, CornerRadius = new CornerRadius(24), Background = Theme.CardTint(colorIndex, rowIndex), BorderThickness = new Thickness(0), Margin = new Thickness(0,0,0,6) };
            pill.Resources["Ink"] = ink; pill.Resources["Ring"] = muted;
            pill.Resources["Hover"] = new SolidColorBrush(Color.FromArgb(26,255,255,255));
            pill.Resources["Pressed"] = new SolidColorBrush(Color.FromArgb(24,0,0,0));
            TrackCompletionCard(item, pill, colorIndex);
            AttachItemReordering(pill, item);
            return pill;
        }
        public void Edit(Entry item, string group) {
            BeginItem(item, group, false);
        }
        void EditGroup(string name) {
            BeginTopic(name);
        }
        bool DeleteGroup(string original) {
            bool pendingRename = inline != null && inline.IsTopic && inline.Group == original;
            if (!FinishInline(false)) return false;
            if (pendingRename && original == renamedFrom) original = renamedTo;
            try {
                var latest = Store.Load(); string next = latest.ChangeGroup(original, null);
                Store.Save(latest.Text, next); RememberUndo(latest.Text, next);
                Refresh(); Notify("Deleted", true, false); return true;
            } catch (Exception ex) { Notify(ex.Message, false, true); return false; }
        }
        static Entry Copy(Entry e) { return new Entry { Title = e.Title, Link = e.Link, Note = e.Note, Group = e.Group, Done = e.Done }; }
        bool Change(Entry old, Entry next, string group, string message) {
            bool sameGroup = old != null && old.Group == group;
            if (!FinishInline(false)) return false;
            if (sameGroup) { group = old.Group; if (next != null) next.Group = group; }
            try {
                if (next != null) NoteStore.Validate(next);
                string before = Store.Mutate(old, next, group); document = Store.Load(); RememberUndo(before, document.Text);
                ShowCompletionFeedback(old, next);
                Render(); Notify(message, true, false); return true;
            } catch (Exception ex) { try { Refresh(); } catch { } Notify(ex.Message, false, true); return false; }
        }
        void Undo() {
            if (!FinishInline(false)) return;
            if (UndoWatchingTitle()) return;
            if (undoBefore == null) return;
            try { Store.Save(undoAfter, undoBefore); RememberUndo(null, null); Refresh(); Notify("Undone", false, false); }
            catch (Exception ex) { Notify(ex.Message, false, true); }
        }
        void Notify(string message, bool undo, bool error) {
            toastTimer.Stop(); toastContent.Children.Clear();
            var text = Theme.Text(message, 11.5, error ? Theme.Error : Theme.Ink);
            text.MaxWidth = Math.Max(150, layout.ActualWidth - 135); text.TextWrapping = TextWrapping.Wrap; toastContent.Children.Add(text);
            if (undo) { var undoButton = Theme.Button("Undo", "Undo (Ctrl+Z)", Undo); undoButton.Margin = new Thickness(9, 0, 0, 0); toastContent.Children.Add(undoButton); }
            var dismiss = Theme.Icon("\uE8BB", "Dismiss", delegate { toast.Visibility = Visibility.Collapsed; }); dismiss.FontSize = 9; toastContent.Children.Add(dismiss);
            toast.Visibility = Visibility.Visible;
            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            if (!error) toastTimer.Start();
        }
        void Open(string link) { if (!FinishInline(false)) return; try { Desktop.Open(link, Store.Vault); } catch (Exception ex) { Notify(ex.Message, false, true); } }
        static MenuItem AddMenu(ItemsControl menu, string label, Action action) { var item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); return item; }
        ContextMenu MoreMenu() {
            var menu = new ContextMenu { FontFamily = FontFamily, FontSize = 12 };
            var glassControls = new StackPanel { Width = 200, Margin = new Thickness(0, 4, 0, 4) };
            var glassLabel = Theme.Text("Transparency  " + config.Transparency.ToString("0") + "%", 12, Theme.Ink); glassControls.Children.Add(glassLabel);
            var slider = new Slider { Minimum = Settings.MinTransparency, Maximum = Settings.MaxTransparency, Value = config.Transparency, TickFrequency = 1, SmallChange = 1, LargeChange = 5, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0, 6, 0, 0) };
            slider.Style = (Style)Resources["TransparencySlider"];
            // Move-to-point handles the preview event before normal focus logic.
            slider.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(delegate(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) slider.Focus(); }), true);
            AutomationProperties.SetName(slider, "Glass transparency");
            bool changed = false;
            var saveDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            Action flush = delegate { saveDelay.Stop(); if (changed) { SaveSettings(); changed = false; } };
            saveDelay.Tick += delegate { flush(); };
            menu.Closed += delegate { flush(); };
            slider.ValueChanged += delegate { config.Transparency = slider.Value; glassLabel.Text = "Transparency  " + slider.Value.ToString("0") + "%"; ApplyGlass(); changed = true; saveDelay.Stop(); saveDelay.Start(); };
            glassControls.Children.Add(slider); menu.Items.Add(new MenuItem { Header = glassControls, StaysOpenOnClick = true, Style = (Style)Resources["MenuControlItem"] });
            menu.Items.Add(new Separator { Style = (Style)Resources[typeof(Separator)] });
            var pinItem = AddMenu(menu, "Always on top", delegate { config.Pinned = !config.Pinned; Topmost = collapsed || config.Pinned; SaveSettings(); }); pinItem.IsCheckable = true; pinItem.IsChecked = config.Pinned;
            if (resident) {
                var startup = AddMenu(menu, "Open at login", delegate { try { SaveSettings(); Desktop.SetStartup(!Desktop.StartupEnabled); } catch (Exception ex) { Notify(ex.Message, false, true); } });
                startup.IsCheckable = true; startup.IsChecked = Desktop.StartupEnabled;
            }
            return menu;
        }
        void Zoom(double delta) { config.Zoom = Math.Max(0.8, Math.Min(1.5, Math.Round(config.Zoom + delta, 1))); ApplyZoom(); }
        void ApplyZoom() { groups.LayoutTransform = new ScaleTransform(config.Zoom, config.Zoom); }
        void OnKey(object sender, KeyEventArgs e) {
            if (IsItemReordering) return;
            if (e.Key == Key.ImeProcessed || (inline != null && inline.Composing)) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.N) { if (document.Groups.Count == 0) EditGroup(null); else Edit(null, document.Groups[0]); }
            else if (e.Key == Key.Z) {
                var input = Keyboard.FocusedElement as TextBox;
                if (input != null) {
                    if (inline == null || input.CanUndo || (!string.IsNullOrWhiteSpace(inline.Text) && !ReferenceEquals(inline, inlineRemovalUndo))) return;
                    inline = null;
                }
                Undo(); Focus();
            }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add) Zoom(0.1);
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) Zoom(-0.1);
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0) { config.Zoom = 1; ApplyZoom(); }
            else return;
            e.Handled = true;
        }
    }
}
