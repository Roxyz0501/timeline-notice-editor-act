using TimelineNoticeEditor;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    private static string artifacts;
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            artifacts = Path.GetFullPath(Path.Combine("artifacts", "tests-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(artifacts);
            XmlSafety(); Images(); Languages(); UpdateSafety(); OverlayStability();
            if (args.Contains("--ui")) Ui();
            var at = Array.IndexOf(args, "--timeline"); if (at >= 0) Integration(args[at + 1]);
            if (args.Contains("--release")) ReleaseIntegration();
            Console.WriteLine("PASS: " + checks + " checks. Artifacts: " + artifacts); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; }
    private static void Throws(Action action, string name) { try { action(); } catch { checks++; return; } throw new Exception("FAIL: " + name); }
    private static string Write(string name, string text, Encoding encoding = null) { var path = Path.Combine(artifacts, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, encoding ?? new UTF8Encoding(false)); return path; }
    private static void XmlSafety()
    {
        const string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<timeline>\r\n<!-- <i-notice left='5'/> -->\r\n<s name='P1'><a time='12' text='Test'>\r\n <i-notice image='sample.png' note=\" left='90' &gt; \" left = '90' top=\"20\" scale='1'/>\r\n <v-notice text='hello' icon='icon.png'/>\r\n <i-notice image='sample.png'/>\r\n</a><script><![CDATA[<i-notice left='1'/>]]></script></s></timeline>";
        var path = Write("timeline.xml", source); var d = new TimelineDocument(path);
        Check(d.Notices.Count == 2, "ignore comments and CDATA"); Check(d.Notices[0].Line == 5 && d.Notices[0].Section == "P1", "line and parent context");
        Check(d.Render() == source && !d.Dirty, "no-op is byte preserving");
        d.Notices[0].Set("left", 120.25); d.Notices[1].Set("top", -25);
        var expected = source.Replace("left = '90'", "left = '120.25'").Replace("<i-notice image='sample.png'/>", "<i-notice image='sample.png' top=\"-25\"/>");
        Check(d.Render() == expected, "only actual attributes changed; attribute-like quoted text preserved");
        Check(d.Notices.All(n => n.IsImage), "only image notices are selectable");
        Throws(() => d.Notices[0].Set("scale", 0), "invalid scale rejected");
        var backup = d.Save(); Check(File.ReadAllText(backup) == source && File.ReadAllText(path) == expected, "atomic save with exact original backup");
        var changed = new TimelineDocument(path); changed.Notices[0].Set("top", 10); File.AppendAllText(path, " ");
        Throws(() => changed.Save(), "external edits protected"); Check(File.ReadAllText(path).EndsWith(" "), "external content retained");
        var unicode = Write("utf16.xml", "<?xml version='1.0' encoding='utf-16'?><timeline><i-notice image='日本語.png' left='1'/></timeline>", Encoding.Unicode);
        var ud = new TimelineDocument(unicode); ud.Notices[0].Set("left", 2); ud.Save(); Check(File.ReadAllBytes(unicode)[0] == 255 && File.ReadAllText(unicode).Contains("日本語.png"), "UTF-16 BOM preserved");
        var cr = new TimelineDocument(Write("cr.xml", "<timeline>\r<i-notice left='1'/>\r</timeline>")); cr.Notices[0].Set("left", 2); Check(cr.Render().Contains("left='2'"), "CR-only line mapping");
        var defaults = new TimelineDocument(Write("defaults.xml", "<timeline><default target-element='ImageNotice' target-attr='left' value='12'/><i-notice/></timeline>")); Check(defaults.Default(defaults.Notices[0], "left", -1) == 12, "inherited position default");
        Throws(() => TimelineDocument.Parse("<!DOCTYPE timeline [<!ENTITY x SYSTEM 'file:///c:/private'>]><timeline>&x;</timeline>"), "DTD/XXE rejected");
        Check(new TimelineDocument(Write("visual-only.xml", "<timeline><v-notice text='' icon='missing.png'/></timeline>")).Notices.Count == 0, "visual-only XML has no editable notices");
    }
    private static void Images()
    {
        var root = Path.Combine(artifacts, "resources"); Directory.CreateDirectory(Path.Combine(root, "images")); Directory.CreateDirectory(Path.Combine(root, "icon", "nested")); Directory.CreateDirectory(Path.Combine(root, "timeline"));
        using (var b = new Bitmap(120, 80)) { using (var g = Graphics.FromImage(b)) { g.Clear(System.Drawing.Color.CornflowerBlue); g.DrawString("Preview", System.Drawing.SystemFonts.DefaultFont, System.Drawing.Brushes.White, 20, 30); } b.Save(Path.Combine(root, "images", "sample.png")); b.Save(Path.Combine(root, "icon", "nested", "icon.png")); }
        var resolver = new ImageResolver(Path.Combine(root, "timeline", "demo.xml"), "");
        Check(resolver.Resolve("sample.png", false) == Path.Combine(root, "images", "sample.png"), "sibling images search");
        Check(resolver.Resolve("icon.png", true) == Path.Combine(root, "icon", "nested", "icon.png"), "nested icon search");
        var image = resolver.LoadAsync("sample.png", false, false, CancellationToken.None).GetAwaiter().GetResult(); Check(image.Item1.PixelWidth == 120 && image.Item1.IsFrozen, "decoded image is detached and frozen");
        var doc = new TimelineDocument(Write("render.xml", "<timeline><i-notice image='sample.png'/></timeline>"));
        var render = NoticePreview.Render(image.Item1, .5, "missing"); Check(render.PixelWidth == 66 && render.PixelHeight == 46, "Hojoring pixel dimensions, scale, 3-DIP border");
        Throws(() => resolver.LoadAsync("https://example.com/image.png", false, false, CancellationToken.None).GetAwaiter().GetResult(), "remote fetch needs explicit click");
        Throws(() => resolver.LoadAsync("http://example.com/image.png", false, true, CancellationToken.None).GetAwaiter().GetResult(), "HTTP remote rejected");
        Check(resolver.Resolve("not-found.png", false) == null, "missing image safe");
    }
    private static void Languages()
    {
        foreach (var pair in new[] { new[] { "ja-JP", "ja" }, new[] { "zh-TW", "zh-CN" }, new[] { "ko-KR", "ko" }, new[] { "fr-FR", "en" } }) Check(Localization.MapCulture(new CultureInfo(pair[0])) == pair[1], "culture " + pair[0]);
        var s = new PluginSettings(); Check(s.InitializeLanguageIfMissing(new CultureInfo("ja-JP")), "first language init"); Check(!s.InitializeLanguageIfMissing(new CultureInfo("en-US")) && s.Language == "ja", "saved language retained");
        var old = PluginSettings.Load(Write("old-config.xml", "<PluginSettings><Language>ko</Language></PluginSettings>")); Check(old.Language == "ko" && old.CheckUpdatesOnStartup, "old config defaults");
        foreach (var lang in new[] { "en", "ja", "zh-CN", "ko" }) { Check(Localization.Get(lang, "SaveXml") != "SaveXml", "localized save button " + lang); Check(Localization.Get(lang, "FallbackProbe") == "English fallback", "fallback " + lang); }
        string error; int opened = 0;
        Check(SettingsControl.TryOpenSupportLink(info => { opened++; Check(info.FileName == "https://ko-fi.com/roxyz0501" && info.UseShellExecute, "support target"); return null; }, out error) && opened == 1, "explicit link launch");
        Check(!SettingsControl.TryOpenSupportLink(info => { throw new IOException(); }, out error), "link failure contained");
    }
    private static void OverlayStability()
    {
        var bitmap = BitmapSource.Create(30, 20, 96, 96, PixelFormats.Bgra32, null, new byte[30 * 20 * 4], 120); bitmap.Freeze();
        var w = new PreviewWindow(); w.Present(bitmap, 120, 130, false, false); var handle = new WindowInteropHelper(w).Handle;
        for (int i = 0; i < 10; i++) { w.Present(bitmap, 120, 130, i % 2 == 0, i % 3 == 0); Check(new WindowInteropHelper(w).Handle == handle && w.Left == 120 && w.Top == 130 && w.Width == 30 && w.Height == 20, "stable preview HWND and bounds"); }
        w.Close();
    }
    private static void Ui()
    {
        Application.EnableVisualStyles();
        var file = Path.Combine(artifacts, "resources", "timeline", "demo.xml"); File.WriteAllText(file, "<timeline><s name='P1'><a time='20' text='Preview image'><i-notice image='sample.png' left='800' top='400'/><v-notice text='Notice' icon='icon.png'/></a></s></timeline>");
        var configFile = Path.Combine(Path.GetDirectoryName(file), "Timeline.config");
        File.WriteAllText(configFile, "<TimelineConfig><NoticeLeft>400</NoticeLeft><NoticeTop>200</NoticeTop><NoticeWidth>404</NoticeWidth></TimelineConfig>");
        foreach (var lang in new[] { "en", "ja", "zh-CN", "ko" })
        {
            using (var form = new Form { Width = 1080, Height = 1320 })
            using (var control = new SettingsControl(new PluginSettings { Language = lang, TimelineFolder = Path.GetDirectoryName(file), CheckUpdatesOnStartup = false }))
            {
                form.Controls.Add(control); form.Show(); var editor = Descendants(control).OfType<EditorControl>().Single(); editor.Open(file);
                var deadline = DateTime.UtcNow.AddSeconds(2); while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                var position = (NumericUpDown)typeof(EditorControl).GetField("left", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(editor);
                position.Value += 10;
                var saveXml = Descendants(editor).OfType<Button>().Single(b => (string)b.Tag == "SaveXml"); Check(saveXml.Enabled, "editing enables XML save"); saveXml.PerformClick();
                Check(File.ReadAllText(file).Contains("left='" + position.Value.ToString("0.###", CultureInfo.InvariantCulture) + "'"), "editor save applies selected XML position");
                var list = Descendants(editor).OfType<ListBox>().Single();
                Check(list.Items.Count == 1, "UI lists only i-notice");
                Check(!Descendants(editor).OfType<Button>().Any(b => (string)b.Tag == "SaveGlobal"), "common config controls removed");
                Check(File.ReadAllText(configFile) == "<TimelineConfig><NoticeLeft>400</NoticeLeft><NoticeTop>200</NoticeTop><NoticeWidth>404</NoticeWidth></TimelineConfig>", "Timeline.config remains untouched");
                Check(File.ReadAllText(file).Contains("<v-notice text='Notice' icon='icon.png'/>"), "v-notice preserved on image save");
                Descendants(editor).OfType<CheckBox>().Single(c => (string)c.Tag == "DetailView").Checked = true;                deadline = DateTime.UtcNow.AddSeconds(1); while (DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
                using (var b = new Bitmap(form.ClientSize.Width, form.ClientSize.Height)) { form.DrawToBitmap(b, form.ClientRectangle); b.Save(Path.Combine(artifacts, "ui-" + lang + ".png")); }
                var tabs = Descendants(control).OfType<TabControl>().Single(); Check(tabs.TabPages.Count == 3 && tabs.TabPages[2].Text == Localization.Get(lang, "Support"), "support tab " + lang);
                foreach (TabPage tab in tabs.TabPages) { tabs.SelectedTab = tab; Application.DoEvents(); Check(tab.Controls.Count > 0, "tab built"); }
                tabs.SelectedIndex = 0; form.Width = 650; form.Height = 730; form.PerformLayout(); Application.DoEvents();
                using (var b = new Bitmap(form.ClientSize.Width, form.ClientSize.Height)) { form.DrawToBitmap(b, form.ClientRectangle); b.Save(Path.Combine(artifacts, "ui-narrow-" + lang + ".png")); }
                form.Close();
            }
        }
    }
    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Integration(string file)
    {
        var original = File.ReadAllBytes(file); var doc = new TimelineDocument(file); var resolver = new ImageResolver(file, ""); int found = 0, missing = 0;
        foreach (var n in doc.Notices)
        {
            var reference = n.Get(n.IsImage ? "image" : "icon"); if (reference.Length == 0) continue;
            var path = resolver.Resolve(reference, !n.IsImage); if (path == null) { missing++; continue; }
            var result = resolver.LoadAsync(reference, !n.IsImage, false, CancellationToken.None).GetAwaiter().GetResult(); Check(result.Item1 != null, "actual referenced image decoded"); found++;
        }
        var copy = Path.Combine(artifacts, "private-integration.xml"); File.WriteAllBytes(copy, original); var editable = new TimelineDocument(copy); var selected = editable.Notices.First(n => n.IsImage); selected.Set("left", 321); var backup = editable.Save();
        Check(File.ReadAllBytes(backup).SequenceEqual(original), "real XML exact backup"); Check(File.ReadAllBytes(file).SequenceEqual(original), "original XML untouched");
        Console.WriteLine("Timeline integration: " + doc.Notices.Count + " notices, " + found + " images resolved, " + missing + " missing.");
    }
    private static void ReleaseIntegration()
    {
        using (var service = new UpdateService())
        {
            var result = service.CheckAsync(new Version(0, 0, 0), CancellationToken.None).GetAwaiter().GetResult(); Check(result.Status == UpdateCheckStatus.UpdateAvailable, "real stable release available");
            var package = service.DownloadAndVerifyAsync(result.Release, CancellationToken.None).GetAwaiter().GetResult(); Check(File.Exists(package.StagedDllPath) && File.Exists(package.StagedUpdaterPath), "real release ZIP, SHA256, DLL and updater verified");
        }
    }
}
