using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Shike;
using Path = System.IO.Path;

// Uses the production TextField factory without showing any window.
class BodyTypographyTests {
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static MainWindow window; static string output; static int checks, failures;
    static readonly List<string> metrics = new List<string> { "sample,role,dpi,zoom,lines,ink_center,circle_center,caret_center,ink_top,ink_bottom,field_top,field_bottom" };
    sealed class Sample { public string Name, Value; public bool Wrapped; public Sample(string n, string v, bool wrap = false) { Name=n; Value=v; Wrapped=wrap; } }
    sealed class Shot { public Grid Root; public TextBox Input; public BitmapSource Bitmap; public byte[] Pixels; public List<Rect> Ink = new List<Rect>(); public List<Rect> Carets = new List<Rect>(); public Rect Bounds; public double Circle, Scale; }
    static string N(double n) { return n.ToString("0.###", CultureInfo.InvariantCulture); }
    static void Check(bool condition, string message) { checks++; if(!condition) failures++; Console.WriteLine((condition?"PASS ":"FAIL ")+message); }
    static TextBox Field(string text, string role, bool display) {
        var type=typeof(MainWindow).GetNestedType("TextFieldRole",BindingFlags.NonPublic);
        return (TextBox)typeof(MainWindow).GetMethod("TextField",Private).Invoke(window,new object[]{text,role=="Heading"?19:role=="Link"?11.5:15.5,Brushes.Black,Enum.Parse(type,role),display});
    }
    static Shot Capture(Sample sample, string role, double dpi, double zoom, bool display) {
        var shot=new Shot {Scale=dpi}; double width=sample.Wrapped?170:390;
        var outer=new Grid { Background=Brushes.White, UseLayoutRounding=true, SnapsToDevicePixels=true };
        var row=new Grid {Width=width,MinHeight=44,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,LayoutTransform=new ScaleTransform(zoom,zoom)};
        row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(30)}); row.ColumnDefinitions.Add(new ColumnDefinition()); outer.Children.Add(row);
        var circle=new Ellipse {Width=16,Height=16,Stroke=Brushes.Black,StrokeThickness=1.2}; row.Children.Add(circle);
        var panel=new StackPanel {Margin=new Thickness(2,6,0,6),VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(panel,1); row.Children.Add(panel);
        shot.Input=Field(sample.Value,role,display); panel.Children.Add(shot.Input);
        TextOptions.SetTextFormattingMode(outer,TextFormattingMode.Display); VisualTreeHelper.SetRootDpi(outer,new DpiScale(dpi,dpi));
        outer.Measure(new Size(width*zoom,1000)); outer.Arrange(new Rect(0,0,width*zoom,Math.Ceiling(outer.DesiredSize.Height))); outer.UpdateLayout();
        shot.Root=outer; shot.Bounds=shot.Input.TransformToAncestor(outer).TransformBounds(new Rect(0,0,shot.Input.ActualWidth,shot.Input.ActualHeight));
        var circleBounds=circle.TransformToAncestor(outer).TransformBounds(new Rect(0,0,16,16)); shot.Circle=circleBounds.Top+circleBounds.Height/2;
        int previous=-1;
        for(int index=0;index<sample.Value.Length;index++) {
            int line=shot.Input.GetLineIndexFromCharacterIndex(index); if(line==previous)continue; previous=line;
            shot.Carets.Add(shot.Input.TransformToAncestor(outer).TransformBounds(shot.Input.GetRectFromCharacterIndex(index)));
        }
        int w=(int)Math.Ceiling(outer.ActualWidth*dpi),h=(int)Math.Ceiling(outer.ActualHeight*dpi);
        var target=new RenderTargetBitmap(w,h,96*dpi,96*dpi,PixelFormats.Pbgra32);target.Render(outer);
        shot.Pixels=new byte[w*h*4];target.CopyPixels(shot.Pixels,w*4,0);
        shot.Bitmap=BitmapSource.Create(w,h,96*dpi,96*dpi,PixelFormats.Pbgra32,null,shot.Pixels,w*4);shot.Bitmap.Freeze();
        var bounds=Enumerable.Range(0,shot.Carets.Count).Select(_=>new[]{int.MaxValue,int.MaxValue,-1,-1}).ToArray();
        for(int y=0;y<h;y++)for(int x=(int)Math.Floor(shot.Bounds.Left*dpi);x<w;x++) {
            if(shot.Pixels[(y*w+x)*4]>165)continue;
            int line=0; double py=(y+.5)/dpi;
            while(line+1<shot.Carets.Count && py>(shot.Carets[line].Top+shot.Carets[line].Height/2+shot.Carets[line+1].Top+shot.Carets[line+1].Height/2)/2)line++;
            if(bounds.Length==0)continue;
            bounds[line][0]=Math.Min(bounds[line][0],x);bounds[line][1]=Math.Min(bounds[line][1],y);bounds[line][2]=Math.Max(bounds[line][2],x);bounds[line][3]=Math.Max(bounds[line][3],y);
        }
        foreach(var b in bounds)shot.Ink.Add(b[2]<0?Rect.Empty:new Rect(b[0]/dpi,b[1]/dpi,(b[2]-b[0]+1)/dpi,(b[3]-b[1]+1)/dpi));
        return shot;
    }
    static void Save(BitmapSource image,string name) { var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file); }
    static void Run(Sample sample,string role,double dpi,double zoom) {
        string name=sample.Name+"-"+role+"-dpi"+N(dpi*100)+"-zoom"+N(zoom);
        var saved=Capture(sample,role,dpi,zoom,true);var editing=Capture(sample,role,dpi,zoom,false);
        Check(saved.Input.FontWeight==(role=="Heading"?FontWeights.Bold:FontWeights.Normal),name+" weight");
        Check(saved.Bounds==editing.Bounds && saved.Pixels.SequenceEqual(editing.Pixels),name+" display/edit identical");
        Check(saved.Ink.Count>0 && saved.Ink.All(x=>!x.IsEmpty),name+" every line has ink");
        Check(saved.Ink.All(x=>x.Top>=saved.Bounds.Top-0.5/dpi && x.Bottom<=saved.Bounds.Bottom+0.5/dpi),name+" glyphs inside line field");
        Check(saved.Ink.Zip(saved.Ink.Skip(1),(a,b)=>b.Top-a.Bottom).All(x=>x*dpi>=1),name+" wrapped lines have clear separation");
        if(!sample.Wrapped && role=="Body") {
            double center=saved.Ink[0].Top+saved.Ink[0].Height/2;
            // Lowercase descenders legitimately extend farther below the optical
            // center than CJK and caps; permit their natural shape, not control drift.
            double tolerance=sample.Name=="cjk" || sample.Name=="caps"?1.2:2.3;
            Check(Math.Abs(center-saved.Circle)<=tolerance*zoom+0.5/dpi,name+" optical center");
            double caret=saved.Carets[0].Top+saved.Carets[0].Height/2;
            Check(Math.Abs(caret-saved.Circle)<=0.8/dpi+0.3*zoom,name+" caret centered on circle");
        }
        var ink=saved.Ink[0];var caret0=saved.Carets[0];
        metrics.Add(string.Join(",",new[]{sample.Name,role,N(dpi),N(zoom),saved.Ink.Count.ToString(),N(ink.Top+ink.Height/2),N(saved.Circle),N(caret0.Top+caret0.Height/2),N(saved.Ink.First().Top),N(saved.Ink.Last().Bottom),N(saved.Bounds.Top),N(saved.Bounds.Bottom)}));
        if(zoom==1 && (dpi==1 || dpi==2)) {Save(saved.Bitmap,name+"-display");Save(editing.Bitmap,name+"-edit");}
    }
    static IEnumerable<DependencyObject> Children(DependencyObject element) {
        yield return element;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)foreach(var child in Children(VisualTreeHelper.GetChild(element,i)))yield return child;
    }
    static object Member(string name) {return typeof(MainWindow).GetField(name,Private).GetValue(window);}
    static object Invoke(string name,params object[] values) {return typeof(MainWindow).GetMethod(name,Private).Invoke(window,values);}
    static Rect InRoot(FrameworkElement element,FrameworkElement root) {return element.TransformToAncestor(root).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));}
    static void ItemAlignment(NoteStore store) {
        foreach(string timer in new[]{"watcher","contrastTimer"})((DispatcherTimer)Member(timer)).Stop();
        var root=(FrameworkElement)window.Content;window.Content=null;root.Resources=Theme.Resources();
        root.SetValue(TextElement.FontFamilyProperty,window.FontFamily);root.SetValue(TextElement.FontSizeProperty,window.FontSize);root.SetValue(TextElement.ForegroundProperty,window.Foreground);
        root.UseLayoutRounding=true;root.SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(root,TextFormattingMode.Display);
        Action layout=delegate {
            root.Measure(new Size(900,640));root.Arrange(new Rect(0,0,900,640));root.UpdateLayout();
            root.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.ApplicationIdle);
            root.Measure(new Size(900,640));root.Arrange(new Rect(0,0,900,640));root.UpdateLayout();
        };
        var samples=new[]{new Entry {Title="",Link=""},new Entry {Title="关于",Link="https://example.test"},new Entry {Title="Project notes",Link="https://example.test/reading-notes/a/long/reference/path"},new Entry {Title="关于阅读计划与所有重要参考资料以及本周要完成的任务",Link="https://example.test"},new Entry {Title="关于",Link="https://example.test",Note="A supporting note that wraps into additional lines without moving the title's checkbox"}};
        foreach(string group in new[]{"Plans",NoteStore.ReadingGroup})foreach(double dpi in new[]{1.0,1.25,1.5,2.0})foreach(double zoom in new[]{.8,1.0,1.5})for(int index=0;index<samples.Length;index++) {
            var settings=(Settings)Member("config");settings.Zoom=zoom;Invoke("ApplyZoom");VisualTreeHelper.SetRootDpi(root,new DpiScale(dpi,dpi));
            var sample=samples[index];sample.Group=group;
            File.WriteAllText(store.CurrentPath,"# Test\n\n## "+group+"\n"+NoteDocument.Serialize(sample,"\n")+"\n",new System.Text.UTF8Encoding(false));window.Refresh();layout();
            Rect before=Rect.Empty; double originalCircle=0;
            for(int phase=0;phase<2;phase++) {
                var checkbox=Children(root).OfType<CheckBox>().First();var ring=(FrameworkElement)checkbox.Template.FindName("RingShape",checkbox);
                TextBox title;
                if(phase==0)title=Children(root).OfType<Button>().Where(x=>AutomationProperties.GetName(x).StartsWith("Edit ")&&!AutomationProperties.GetName(x).StartsWith("Edit link ")).SelectMany(Children).OfType<TextBox>().First();
                else {object draft=Member("inline");title=(TextBox)draft.GetType().GetField("Input").GetValue(draft);}
                Rect caret=title.GetRectFromCharacterIndex(0);Check(!caret.IsEmpty,group+" first line is available, sample"+index+" phase"+phase);
                double target=title.TransformToAncestor(root).Transform(new Point(0,caret.Top+caret.Height/2)).Y;
                Rect bounds=InRoot(ring,root);double center=bounds.Top+bounds.Height/2;
                string name=group+" sample"+index+" dpi"+N(dpi*100)+" zoom"+N(zoom)+" phase"+phase;
                Check(Math.Abs(center-target)*dpi<=.8,name+" circle aligns to first title line (delta "+N(center-target)+" DIP)");
                FrameworkElement card=title;
                while(!(card is Border && ((Border)card).Background is LinearGradientBrush))card=(FrameworkElement)VisualTreeHelper.GetParent(card);
                if(phase==0){before=InRoot(card,root);originalCircle=center;var entry=((NoteDocument)Member("document")).Entries.First();window.Edit(entry,entry.Group);layout();}
                else {Check(InRoot(card,root)==before && Math.Abs(center-originalCircle)*dpi<=.8,name+" editing preserves card and circle bounds");Invoke("FinishInline",false);layout();}
            }
        }
    }
    [STAThread] static int Main(string[] args) {
        output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        try {
            string state=Path.Combine(output,"state");var store=new NoteStore(Path.Combine(state,"vault"),Path.Combine(state,"backups"));store.Initialize();window=new MainWindow(store,new Settings {Width=900,Height=640,Pinned=false},state);
            var samples=new[]{new Sample("cjk","关于"),new Sample("latin","Project notes"),new Sample("descenders","Agyp"),new Sample("mixed","关于 Agyp"),new Sample("caps","ABOUT"),new Sample("accents","Éléphant Ångström"),new Sample("wrapped-cjk","关于阅读计划：本周需要逐项完成的任务以及重要资料",true),new Sample("wrapped-accents","Éléphant Ångström jÁÉgj jumps quickly past mixed 关于 and reading notes",true)};
            foreach(double dpi in new[]{1.0,1.25,1.5,2.0})foreach(double zoom in new[]{.8,1.0,1.5})foreach(var sample in samples)foreach(string role in new[]{"Body","Link"})Run(sample,role,dpi,zoom);
            foreach(double dpi in new[]{1.0,1.25,1.5,2.0})Run(new Sample("heading","Research 关于"),"Heading",dpi,1);
            ItemAlignment(store);
            Console.WriteLine(checks+" typography checks; "+failures+" failures.");return failures==0?0:1;
        } catch(Exception ex) {Console.Error.WriteLine(ex.GetType().FullName+": "+ex.Message);if(ex.InnerException!=null)Console.Error.WriteLine(ex.InnerException.GetType().FullName+": "+ex.InnerException.Message);return 2;}
        finally {File.WriteAllLines(Path.Combine(output,"metrics.csv"),metrics);if(window!=null)window.Exit();app.Shutdown();}
    }
}
