using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Shike;

static class ResizeHitTests {
    static readonly MethodInfo Hit = typeof(Desktop).GetMethod("ResizeHit", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly MethodInfo Interior = typeof(Desktop).GetMethod("IsInteriorResizeCorner", BindingFlags.Static | BindingFlags.NonPublic);
    static int checks;
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject value) {
        yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(value); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(value, i))) yield return child;
    }
    static bool Under(IInputElement hit, DependencyObject expected) {
        for (var node = hit as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node)) if (node == expected) return true;
        return false;
    }
    static IInputElement VisualInputHit(Visual root, Point point) {
        // A deliberately unshown Window leaves inherited IsVisible=false.
        // Use the measured visual tree while honoring each local input surface.
        DependencyObject found = null;
        VisualTreeHelper.HitTest(root, delegate(DependencyObject value) {
            var element = value as UIElement;
            return element != null && (!element.IsHitTestVisible || element.Visibility != Visibility.Visible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue;
        }, delegate(HitTestResult result) { found = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        for (; found != null && !(found is IInputElement); found = VisualTreeHelper.GetParent(found)) { }
        return found as IInputElement;
    }
    static int Result(Point point, Point size, IInputElement input) { return (int)Hit.Invoke(null, new object[] { point, size, input }); }
    [STAThread] static int Main(string[] args) {
        Application app = null; MainWindow window = null;
        try {
            string output = Path.GetFullPath(args[0]);
            var store = new NoteStore(Path.Combine(output, "vault"), Path.Combine(output, "backups")); store.Initialize();
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            window = new MainWindow(store, new Settings { Width = 800, Height = 480 }, output);
            var root = (FrameworkElement)window.Content; window.Content = null; root.Resources = Theme.Resources();
            root.Measure(new Size(800, 480)); root.Arrange(new Rect(0, 0, 800, 480)); root.UpdateLayout();
            var size = new Point(800, 480);
            var close = Descendants(root).OfType<Button>().First(x => AutomationProperties.GetName(x) == "Close panel");
            var add = Descendants(root).OfType<Button>().First(x => AutomationProperties.GetName(x) == "New topic");
            Point closePoint = new Point(780, 16), addPoint = add.TranslatePoint(new Point(add.ActualWidth / 2, add.ActualHeight / 2), root);
            var closeHit = VisualInputHit(root, closePoint); var addHit = VisualInputHit(root, addPoint);
            Check(Under(closeHit, close), "The upper-right corner overlap point is inside the actual Close button");
            Check(Result(closePoint, size, closeHit) == 0, "The inner corner leaves the Close button click in the client area");
            Check(Under(addHit, add), "The measured New topic position hits its actual button");
            Check(Result(addPoint, size, addHit) == 0, "New topic remains in the client area after moving beside the topics");
            Point[] corners = { new Point(14, 14), new Point(790, 14), new Point(14, 466), new Point(786, 466) };
            int[] expected = { 13, 14, 16, 17 };
            for (int i = 0; i < corners.Length; i++) Check(Result(corners[i], size, VisualInputHit(root, corners[i])) == expected[i], "Unused corner surface retains diagonal resize direction " + expected[i]);
            Check(Result(new Point(798, 16), size, closeHit) == 14 && Result(new Point(2, 464), size, addHit) == 16, "The outer seven-DIP edges keep diagonal resize priority over controls");
            Check(Result(new Point(2, 240), size, null) == 10 && Result(new Point(798, 240), size, null) == 11 && Result(new Point(400, 2), size, null) == 12 && Result(new Point(400, 478), size, null) == 15, "All four ordinary edge resize directions remain unchanged");
            Check(Result(new Point(16, 16), size, new TextBox()) == 0 && Result(new Point(16, 16), size, new Thumb()) == 0 && Result(new Point(16, 16), size, new CheckBox()) == 0, "Editable fields, drag thumbs and checkbox actions are protected in inner corners");
            Check(!(bool)Interior.Invoke(null, new object[] { new Point(400, 240), size }) && !(bool)Interior.Invoke(null, new object[] { new Point(2, 240), size }), "Window center and ordinary edges bypass WPF descendant hit testing");
            Console.WriteLine(checks + " resize hit checks passed without showing a window."); return 0;
        } catch (Exception ex) { for (; ex != null; ex = ex.InnerException) Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message); return 1; }
        finally { if (window != null) window.Close(); if (app != null) app.Shutdown(); }
    }
}
