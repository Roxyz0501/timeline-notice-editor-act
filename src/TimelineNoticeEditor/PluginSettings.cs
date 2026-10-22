using System;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using System.Linq;
namespace TimelineNoticeEditor
{
    public sealed class PluginSettings
    {
        public string Language { get; set; }
        public string TimelineFolder { get; set; } = "";
        public string ExtraImageFolder { get; set; } = "";
        public bool CheckUpdatesOnStartup { get; set; } = true;
        public string SkippedVersion { get; set; } = "";
        internal string BackupDirectory { get; set; }
        internal Func<string> BackupDirectoryResolver { get; set; }
        public void Normalize() { if (!string.IsNullOrWhiteSpace(Language)) Language = Localization.NormalizeLanguage(Language); }
        public bool InitializeLanguageIfMissing(CultureInfo culture) { if (!string.IsNullOrWhiteSpace(Language)) return false; Language = Localization.MapCulture(culture); return true; }
        public static PluginSettings Load(string path) { try { using (var stream = File.OpenRead(path)) { var s = (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream); s.Normalize(); return s; } } catch { return new PluginSettings(); } }
        internal byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            {
                new XmlSerializer(typeof(PluginSettings)).Serialize(stream, this);
                return stream.ToArray();
            }
        }

        internal static void SaveSerialized(string path, byte[] content, byte[] expectedDiskContent)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Settings path is required.", "path");
            if (content == null) throw new ArgumentNullException("content");
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            var exists = File.Exists(fullPath);
            if (expectedDiskContent == null)
            {
                if (exists) throw new IOException("Settings file appeared outside this plugin.");
            }
            else if (!exists || !File.ReadAllBytes(fullPath).SequenceEqual(expectedDiskContent))
            {
                throw new IOException("Settings file changed outside this plugin.");
            }

            var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(true);
                }

                if (expectedDiskContent == null) File.Move(temp, fullPath);
                else File.Replace(temp, fullPath, fullPath + ".bak");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        internal static string DiscoverFolder()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Plugins");
            if (!Directory.Exists(root)) return "";
            return System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.Where(System.Linq.Enumerable.Select(Directory.GetDirectories(root, "ACT.Hojoring*"), p => Path.Combine(p, "resources", "timeline")), Directory.Exists)) ?? "";
        }
    }
}
