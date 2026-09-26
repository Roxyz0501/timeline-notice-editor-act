using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TimelineNoticeEditor
{
    // Change only the selected start-tag attributes. Comments, scripts, whitespace and
    // unrelated attributes remain byte-for-byte equivalent in the original encoding.
    internal sealed class TimelineDocument
    {
        internal readonly SafeTextFile File;
        internal readonly XDocument Xml;
        internal readonly List<Notice> Notices = new List<Notice>();
        internal bool Dirty => Notices.Any(n => n.Changes.Count != 0);
        internal TimelineDocument(string path, string backupDirectory = null)
        {
            File = new SafeTextFile(path, backupDirectory); Xml = Parse(File.Text);
            if (Xml.Root?.Name.LocalName != "timeline") throw new InvalidDataException("InvalidTimeline");
            var lines = new List<int> { 0 };
            for (int i = 0; i < File.Text.Length; i++)
                if (File.Text[i] == '\n' || File.Text[i] == '\r') { if (File.Text[i] == '\r' && i + 1 < File.Text.Length && File.Text[i + 1] == '\n') i++; lines.Add(i + 1); }
            foreach (var e in Xml.Descendants().Where(e => e.Name.LocalName == "i-notice"))
            {
                var info = (IXmlLineInfo)e;
                var start = lines[info.LineNumber - 1] + info.LinePosition - 2;
                var end = start; char quote = '\0';
                for (; end < File.Text.Length; end++)
                {
                    var c = File.Text[end];
                    if (quote != '\0') { if (c == quote) quote = '\0'; }
                    else if (c == '\'' || c == '"') quote = c;
                    else if (c == '>') break;
                }
                if (start < 0 || File.Text[start] != '<' || end == File.Text.Length) throw new InvalidDataException("InvalidTimeline");
                Notices.Add(new Notice(e, start, end - start + 1, info.LineNumber, Notices.Count + 1));
            }
        }
        internal static XDocument Parse(string text)
        {
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 }))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        internal string Render()
        {
            var text = File.Text;
            foreach (var n in Notices.Where(n => n.Changes.Count != 0).OrderByDescending(n => n.Start))
            {
                var tag = text.Substring(n.Start, n.Length);
                foreach (var pair in n.Changes)
                {
                    tag = ReplaceAttribute(tag, pair.Key, pair.Value);
                }
                text = text.Remove(n.Start, n.Length).Insert(n.Start, tag);
            }
            Parse(text); return text;
        }
        internal string Save(string backupDirectory = null) => File.Save(Render(), backupDirectory: backupDirectory);
        private static string ReplaceAttribute(string tag, string name, string value)
        {
            int position = 1; while (position < tag.Length && !char.IsWhiteSpace(tag[position]) && tag[position] != '/' && tag[position] != '>') position++;
            var pattern = new Regex(@"\G\s+(?<name>[^\s=/>]+)\s*=\s*(?<q>['""])(?<value>.*?)\k<q>", RegexOptions.Singleline);
            while (position < tag.Length)
            {
                var match = pattern.Match(tag, position); if (!match.Success) break;
                if (match.Groups["name"].Value == name) { var part = match.Groups["value"]; return tag.Remove(part.Index, part.Length).Insert(part.Index, value); }
                position = match.Index + match.Length;
            }
            int insert = tag.Length - (tag.EndsWith("/>", StringComparison.Ordinal) ? 2 : 1);
            return tag.Insert(insert, " " + name + "=\"" + value + "\"");
        }
        internal double Default(Notice n, string attribute, double fallback)
        {
            var type = "ImageNotice";
            var d = Xml.Root.Elements("default").LastOrDefault(e => string.Equals((string)e.Attribute("target-element"), type, StringComparison.OrdinalIgnoreCase) && (string)e.Attribute("target-attr") == attribute);
            return Notice.Number((string)d?.Attribute("value"), fallback);
        }
    }

    internal sealed class Notice
    {
        internal readonly XElement Element;
        internal readonly int Start, Length, Line, Id;
        internal readonly Dictionary<string, string> Changes = new Dictionary<string, string>();
        internal Notice(XElement e, int start, int length, int line, int id) { Element = e; Start = start; Length = length; Line = line; Id = id; }
        internal bool IsImage => Element.Name.LocalName == "i-notice";
        internal string Get(string key) => Changes.ContainsKey(key) ? Changes[key] : (string)Element.Attribute(key) ?? "";
        internal void Set(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 100000 || (key == "scale" && (value <= 0 || value > 20))) throw new ArgumentOutOfRangeException(key);
            if (!IsImage || (key != "left" && key != "top" && key != "scale")) throw new InvalidOperationException("InvalidAttribute");
            var formatted = value.ToString("0.###", CultureInfo.InvariantCulture);
            if (GetOriginal(key) == formatted) Changes.Remove(key); else Changes[key] = formatted;
        }
        private string GetOriginal(string key) => (string)Element.Attribute(key) ?? "";
        internal string Section => (string)Element.Ancestors("s").FirstOrDefault()?.Attribute("name") ?? "";
        internal string Context => (string)Element.Parent?.Attribute("text") ?? (string)Element.Parent?.Attribute("notice") ?? (string)Element.Parent?.Attribute("sync") ?? "";
        internal string Time => (string)Element.Parent?.Attribute("time") ?? "";
        internal static double Number(string value, double fallback) { double n; return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback; }
        public override string ToString() => (Changes.Count > 0 ? "* " : "") + "L" + Line + "  " + Section + "  " + Time + "  " + Element.Name.LocalName + "  " + Context + "  " + Get(IsImage ? "image" : "text");
    }

    internal sealed class SafeTextFile
    {
        internal readonly string PathName;
        internal readonly string Text;
        private readonly byte[] original;
        private readonly Encoding encoding;
        private readonly bool bom;
        private readonly string backupDirectory;
        internal SafeTextFile(string path, string backupDirectory)
        {
            this.backupDirectory = backupDirectory;
            PathName = Path.GetFullPath(path);
            if (new FileInfo(PathName).Length > 32 * 1024 * 1024) throw new InvalidDataException("FileTooLarge");
            original = System.IO.File.ReadAllBytes(PathName);
            using (var reader = new StreamReader(new MemoryStream(original), new UTF8Encoding(false, true), true)) { Text = reader.ReadToEnd(); encoding = reader.CurrentEncoding; }
            var preamble = encoding.GetPreamble(); bom = preamble.Length > 0 && original.Take(preamble.Length).SequenceEqual(preamble);
            var xml = TimelineDocument.Parse(Text);
            if (!string.IsNullOrWhiteSpace(xml.Declaration?.Encoding) && Encoding.GetEncoding(xml.Declaration.Encoding).CodePage != encoding.CodePage)
                throw new InvalidDataException("EncodingUnsupported");
        }
        internal void VerifyUnchanged() { if (!System.IO.File.ReadAllBytes(PathName).SequenceEqual(original)) throw new IOException("ExternalChange"); }
        private string WriteBackup(string directoryOverride)
        {
            var root = directoryOverride ?? backupDirectory;
            if (string.IsNullOrWhiteSpace(root)) throw new IOException("BackupUnavailable");
            string id;
            using (var sha = System.Security.Cryptography.SHA256.Create()) id = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PathName.ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
            var directory = Path.Combine(Path.GetFullPath(root), Path.GetFileName(PathName) + "-" + id);
            Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(Path.Combine(directory, "source-path.txt"), PathName, new UTF8Encoding(false));
            var path = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".bak");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(original, 0, original.Length); stream.Flush(true); }
            if (!System.IO.File.ReadAllBytes(path).SequenceEqual(original)) throw new IOException("BackupUnavailable");
            return path;
        }
        internal string Save(string text, Action<FileStream, byte[]> writer = null, string backupDirectory = null)
        {
            TimelineDocument.Parse(text); VerifyUnchanged();
            var bytes = encoding.GetBytes(text);
            if (bom) bytes = encoding.GetPreamble().Concat(bytes).ToArray();
            // No sidecar files are ever created in the timeline folder. A durable,
            // verified backup is required before touching the exclusively locked file.
            using (var target = new FileStream(PathName, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var current = new byte[target.Length]; int read = 0, n;
                while (read < current.Length && (n = target.Read(current, read, current.Length - read)) > 0) read += n;
                if (!current.SequenceEqual(original)) throw new IOException("ExternalChange");
                var backup = WriteBackup(backupDirectory);
                try
                {
                    target.Position = 0;
                    if (writer == null) target.Write(bytes, 0, bytes.Length); else writer(target, bytes);
                    target.SetLength(bytes.Length); target.Flush(true);
                    target.Position = 0; var verified = new byte[bytes.Length]; int total = 0;
                    while (total < verified.Length && (n = target.Read(verified, total, verified.Length - total)) > 0) total += n;
                    if (!verified.SequenceEqual(bytes)) throw new IOException("SaveFailed");
                }
                catch { target.Position = 0; target.Write(original, 0, original.Length); target.SetLength(original.Length); target.Flush(true); throw; }
                return backup;
            }
        }
    }
}
