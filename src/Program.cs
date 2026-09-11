using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Shike {
    public static class Program {
        [STAThread] public static void Main(string[] args) {
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            string stateDir = Settings.DefaultDirectory;
            string vault = null, screenshot = null; bool exitAfter = false, autostart = false, quit = false;
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--state-dir" && i + 1 < args.Length) stateDir = args[++i];
                else if (args[i] == "--vault" && i + 1 < args.Length) vault = args[++i];
                else if (args[i] == "--screenshot" && i + 1 < args.Length) screenshot = args[++i];
                else if (args[i] == "--exit-after-capture") exitAfter = true;
                else if (args[i] == "--autostart") autostart = true;
                else if (args[i] == "--quit") quit = true;
            }
            Directory.CreateDirectory(stateDir);
            try {
                var config = Settings.LoadForLaunch(stateDir, vault);
                string key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(config.Vault).ToLowerInvariant())).Replace('/', '_');
                bool created;
                using (var mutex = new Mutex(true, "Local\\Shike-" + key, out created))
                using (var restoreEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Shike-Show-" + key))
                using (var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Shike-Quit-" + key)) {
                    if (!created) { (quit ? quitEvent : restoreEvent).Set(); return; }
                    if (quit) return;
                    config.ApplyLaunchLayout();
                    var store = new NoteStore(config.Vault, Path.Combine(stateDir, "backups")); store.Initialize();
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                        File.AppendAllText(Path.Combine(stateDir, "error.log"), DateTime.Now.ToString("s") + " " + e.Exception + "\n");
                        MessageBox.Show("Something went wrong:\n" + e.Exception.Message, "To-do"); e.Handled = true;
                    };
                    var window = new MainWindow(store, config, stateDir, vault == null); window.ShowActivated = !autostart; app.MainWindow = window;
                    var restoreWait = ThreadPool.RegisterWaitForSingleObject(restoreEvent, delegate { window.Dispatcher.BeginInvoke(new Action(window.Restore)); }, null, Timeout.Infinite, false);
                    var quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, delegate { window.Dispatcher.BeginInvoke(new Action(window.Exit)); }, null, Timeout.Infinite, false);
                    if (screenshot != null) {
                        window.Loaded += delegate {
                            var captureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                            captureTimer.Tick += delegate { captureTimer.Stop(); try { Desktop.Screenshot(window, screenshot); } catch (InvalidOperationException ex) { File.AppendAllText(Path.Combine(stateDir, "capture.log"), ex.Message); } if (exitAfter) window.Close(); }; captureTimer.Start();
                        };
                    }
                    try { app.Run(window); } finally { restoreWait.Unregister(null); quitWait.Unregister(null); mutex.ReleaseMutex(); }
                }
            } catch (Exception ex) {
                File.AppendAllText(Path.Combine(stateDir, "error.log"), DateTime.Now.ToString("s") + " " + ex + "\n");
                MessageBox.Show("Could not open your list:\n" + ex.Message, "To-do", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
