using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Shike;
class RenderPreview {
    [STAThread] static void Main(string[] args) {
        var app = new Application();
        string vault = Path.Combine(Path.GetTempPath(), "shike-render-" + Guid.NewGuid().ToString("N"));
        var store = new NoteStore(vault, Path.Combine(vault, "backups")); store.Initialize();
        if (args.Length > 1 && args[1] == "layout") File.WriteAllText(store.CurrentPath, "# Preview\n\n## Topic one\n- [ ] First item\n- [ ] Second item\n\n## Topic two\n\n## Topic three\n\n## To read\n");
        Theme.SetDark(false);
        var window = new MainWindow(store, new Settings { Width = 860, Height = 330, Transparency = 86 }, vault);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(860,330)); content.Arrange(new Rect(0,0,860,330)); content.UpdateLayout();
        app.Dispatcher.Invoke(DispatcherPriority.Render,new Action(delegate{}));
        content.Measure(new Size(860,330)); content.Arrange(new Rect(0,0,860,330)); content.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(225,230,226),Color.FromRgb(195,211,224),30),null,new Rect(0,0,860,330));
            dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,860,330));
        }
        var bitmap = new RenderTargetBitmap(1720,660,192,192,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var file = File.Create(args[0])) encoder.Save(file);
        window.Exit(); app.Shutdown();
    }
}
