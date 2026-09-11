using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace Shike {
    public sealed class PanelLayout {
        public double Left = -1, Top = -1, Width = 860, Height = 330, InterfaceScale = 1;
    }
    public sealed class Settings {
        public const double MinTransparency = 35, MaxTransparency = 95;
        public static string DefaultDirectory { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData"); } }
        public string Vault;
        public bool PortableNotes;
        public string WatchingTitle = NoteStore.ReadingGroup;
        public double WatchingWidth = 0;
        public double InterfaceScale = 1;
        public PanelLayout LaunchLayout;
        public double Left = -1, Top = -1, Width = 860, Height = 330, Zoom = 1, Transparency = 45;
        public bool Pinned = false;
        public int DesignVersion = 0;
        public static double ClampTransparency(double value) {
            return double.IsNaN(value) || double.IsInfinity(value) ? 45 : Math.Max(MinTransparency, Math.Min(MaxTransparency, value));
        }
        public static double ClampInterfaceScale(double value) {
            return double.IsNaN(value) || double.IsInfinity(value) ? 1 : Math.Max(0.65, Math.Min(1.8, value));
        }
        public void Migrate() {
            Transparency = ClampTransparency(Transparency);
            InterfaceScale = ClampInterfaceScale(InterfaceScale);
            if (DesignVersion >= 2) return;
            Width = 860; Height = 330; Left = Top = -1; Pinned = false; Transparency = 45; DesignVersion = 2;
        }
        public void ApplyLaunchLayout() {
            if (LaunchLayout == null) LaunchLayout = new PanelLayout { Left = Left, Top = Top, Width = Width, Height = Height, InterfaceScale = InterfaceScale };
            Left = LaunchLayout.Left; Top = LaunchLayout.Top;
            Width = LaunchLayout.Width; Height = LaunchLayout.Height;
            InterfaceScale = ClampInterfaceScale(LaunchLayout.InterfaceScale);
        }
        public static Settings Load(string directory) {
            string path = Path.Combine(directory, "settings.json");
            string text;
            for (int attempt = 0; ; attempt++) {
                try {
                    // A reader keeps its complete file snapshot while a writer
                    // atomically replaces the directory entry with the next one.
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true)) text = reader.ReadToEnd();
                    break;
                }
                catch (FileNotFoundException) {
                    // Some filesystem providers briefly hide the target while
                    // replacing it. Confirm absence before choosing defaults.
                    if (attempt == 4) return new Settings();
                }
                catch (DirectoryNotFoundException) { return new Settings(); }
                catch (IOException) {
                    if (attempt == 4) throw;
                }
                Thread.Sleep(35);
            }
            try {
                var settings = new JavaScriptSerializer().Deserialize<Settings>(text);
                if (settings == null) throw new InvalidDataException("The settings file is empty or invalid. It has been left unchanged.");
                return settings;
            } catch (Exception ex) {
                var cause = ex;
                while (cause.InnerException != null) cause = cause.InnerException;
                if (!(cause is ArgumentException || cause is InvalidOperationException || cause is FormatException || cause is OverflowException)) throw;
                throw new InvalidDataException("The settings file is invalid. It has been left unchanged.", ex);
            }
        }
        public void Save(string directory) {
            string text = new JavaScriptSerializer().Serialize(this);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "settings.json");
            string temporary = path + ".shike-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static Settings LoadForLaunch(string directory, string explicitVault) {
            var settings = Load(directory);
            settings.Migrate();
            // Resolve portable notes against the current copy even after moving
            // UserData. Existing external paths remain explicit choices.
            if (explicitVault != null) {
                settings.PortableNotes = false;
                settings.Vault = explicitVault;
            } else if (settings.PortableNotes || settings.Vault == null) {
                settings.PortableNotes = true;
                settings.Vault = Path.Combine(directory, "notes");
            }
            return settings;
        }
    }
}
