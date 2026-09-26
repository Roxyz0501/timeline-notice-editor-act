using System;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
namespace TimelineNoticeEditor
{
    public sealed class PluginSettings
    {
        public string Language { get; set; }
        public string TimelineFolder { get; set; } = "";
        public string ExtraImageFolder { get; set; } = "";
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string SkippedVersion { get; set; } = "";
        public void Normalize() { if (!string.IsNullOrWhiteSpace(Language)) Language = Localization.NormalizeLanguage(Language); }
        public bool InitializeLanguageIfMissing(CultureInfo culture) { if (!string.IsNullOrWhiteSpace(Language)) return false; Language = Localization.MapCulture(culture); return true; }
        public static PluginSettings Load(string path) { try { using (var stream = File.OpenRead(path)) { var s = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream); s.Normalize(); return s; } } catch { return new PluginSettings(); } }
        public void Save(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)); var temp = path + ".tmp"; using (var stream = File.Create(temp)) new XmlSerializer(typeof(PluginSettings)).Serialize(stream, this); if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path); }
        internal static string DiscoverFolder()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Plugins");
            if (!Directory.Exists(root)) return "";
            return System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.Where(System.Linq.Enumerable.Select(Directory.GetDirectories(root, "ACT.Hojoring*"), p => Path.Combine(p, "resources", "timeline")), Directory.Exists)) ?? "";
        }
    }
}
