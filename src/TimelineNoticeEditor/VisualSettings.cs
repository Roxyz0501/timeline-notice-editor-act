using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
namespace TimelineNoticeEditor
{
    internal sealed class VisualSettings
    {
        internal readonly SafeTextFile File;
        internal readonly XDocument Xml;
        internal double Left, Top;
        internal readonly double OriginalLeft, OriginalTop;
        internal bool Dirty => Left != OriginalLeft || Top != OriginalTop;
        internal VisualSettings(string path)
        {
            File = new SafeTextFile(path); Xml = TimelineDocument.Parse(File.Text);
            if (Xml.Root?.Name.LocalName != "TimelineConfig") throw new InvalidDataException("InvalidConfig");
            Left = OriginalLeft = Value("NoticeLeft", 0); Top = OriginalTop = Value("NoticeTop", 0);
        }
        internal double Value(string name, double fallback) => Notice.Number((string)Xml.Root.Element(name), fallback);
        internal XElement Style(Notice notice)
        {
            var styles = Xml.Root.Element("Styles")?.Elements("Style").ToArray() ?? new XElement[0];
            return styles.FirstOrDefault(s => (string)s.Element("Name") == notice.Get("style")) ?? styles.FirstOrDefault(s => (string)s.Element("IsDefaultNotice") == "true");
        }
        internal string Save()
        {
            File.VerifyUnchanged();
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ACT.SpecialSpellTimer.RaidTimeline.TimelineSettings", false)).FirstOrDefault(t => t != null);
            if (type == null)
            {
                var text = File.Text;
                foreach (var pair in new[] { new { Name = "NoticeLeft", Value = Left }, new { Name = "NoticeTop", Value = Top } })
                {
                    var node = Xml.Root.Element(pair.Name);
                    if (node == null) throw new InvalidDataException("InvalidConfig");
                    var pattern = "(<" + pair.Name + ">)[^<]*(</" + pair.Name + ">)";
                    text = System.Text.RegularExpressions.Regex.Replace(text, pattern, m => m.Groups[1].Value + pair.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + m.Groups[2].Value);
                }
                return File.Save(text);
            }
            // Use Hojoring's public setters on ACT's UI thread so its in-memory state
            // cannot overwrite the new position later. Its setters auto-save.
            var actualPath = type.GetField("FileName", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
            if (actualPath == null || !string.Equals(Path.GetFullPath(actualPath), File.PathName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("LiveConfigMismatch");
            var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var x = type.GetProperty("NoticeLeft"); var y = type.GetProperty("NoticeTop");
            if (instance == null || x == null || y == null) throw new InvalidOperationException("LiveConfigMismatch");
            var oldX = (double)x.GetValue(instance); var oldY = (double)y.GetValue(instance);
            if (oldX != OriginalLeft || oldY != OriginalTop) throw new IOException("ExternalChange");
            var backup = File.Backup();
            try
            {
                x.SetValue(instance, Left); y.SetValue(instance, Top);
                if ((double)x.GetValue(instance) != Left || (double)y.GetValue(instance) != Top) throw new InvalidOperationException("OverlayLocked");
                type.GetMethod("Save", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null).Invoke(null, null);
                var saved = new VisualSettings(File.PathName);
                if (saved.Left != Left || saved.Top != Top) throw new IOException("SaveFailed");
                return backup;
            }
            catch
            {
                try { x.SetValue(instance, oldX); y.SetValue(instance, oldY); } finally { System.IO.File.Copy(backup, File.PathName, true); }
                throw;
            }
        }
    }
}
