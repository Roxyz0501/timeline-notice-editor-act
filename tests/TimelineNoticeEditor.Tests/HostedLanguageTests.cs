using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TimelineNoticeEditor;
using Roxyz.Act.UI;

internal static partial class Program
{
    private sealed class BackgroundTestForm : Form {
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
    }
    private static void HostedLanguage()
    {
        var settings = new PluginSettings { Language = "ja", TimelineFolder = artifacts, CheckUpdatesOnStartup = false };
        var bytes = settings.Serialize();
        using (var control = new SettingsControl(settings))
        using (var form = new BackgroundTestForm { ClientSize = new Size(420, 620), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-16000, -16000) })
        {
            form.Controls.Add(control); form.Show();
            var tabs = Descendants(control).OfType<PluginTabs>().Single();
            var update = tabs.TabPages.OfType<PluginUpdatePage>().Single();
            var language = tabs.TabPages.OfType<PluginLanguagePage>().Single();
            var support = tabs.TabPages[tabs.TabPages.Count - 1];
            var dynamicPage = new TabPage("Dynamic"); tabs.AddContentPage(dynamicPage);
            Check(tabs.TabPages[tabs.TabPages.Count - 1] == support, "support remains last after dynamic addition");
            var updateFont = update.Font;
            int languageEvents = 0; language.LanguageChanged += delegate { languageEvents++; };
            foreach (var code in new[] { "ja", "en", "zh-CN", "ko", "ja", "ja" })
            {
                control.SetHostedLanguage(code);
                Check(settings.EffectiveLanguage == code && settings.Language == "ja" && settings.Serialize().SequenceEqual(bytes), "host language never persists " + code);
                Check(Descendants(control).OfType<Button>().Any(x => (string)x.Tag == "OpenXml" && x.Text == Localization.Get(code, "OpenXml")), "editor responds to hosted language " + code);
                tabs.SelectedTab = update;
                foreach (var width in new[] { 360, 720 })
                {
                    form.ClientSize = new Size(width, 620); form.CreateControl(); control.CreateControl(); tabs.CreateControl();
                    foreach (UpdatePageState state in Enum.GetValues(typeof(UpdatePageState)))
                    {
                        update.ShowState(state, code + " " + state);
                        using (var bitmap = new Bitmap(width, 620)) control.DrawToBitmap(bitmap, new Rectangle(0, 0, width, 620));
                    }
                }
            }
            Check(languageEvents == 0 && ReferenceEquals(update.Font, updateFont), "language sync silent and shared font reused on round trip");
            tabs.TabPages.Remove(language); tabs.TabPages.Remove(update); tabs.TabPages.Remove(support);
            control.SetHostedLanguage("ko"); control.ApplyTo(settings);
            Check(tabs.TabPages.Count == 2 && settings.Language == "ja", "portal-hidden pages remain hidden without changing standalone language");
            control.SetHostedLanguage(null);
            tabs.AddContentPage(language); tabs.AddContentPage(update); tabs.AddContentPage(support);
            Check(language.LanguageCode == "ja" && settings.EffectiveLanguage == "ja", "standalone language restored after host override");
            var selector = Descendants(language).OfType<ComboBox>().Single(); selector.SelectedIndex = 0;
            Check(settings.Language == "en" && languageEvents == 1, "standalone language selection persists once");
            int checksRequested = 0, installs = 0, laters = 0;
            control.CheckUpdatesRequested += delegate { checksRequested++; };
            control.InstallUpdateRequested += delegate { installs++; };
            control.LaterRequested += delegate { laters++; };
            SemVersion version; SemVersion.TryParse("9.0.0", out version);
            control.ShowUpdateResult(new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = new ReleaseInfo { Version = version, Notes = "Test changes" } }, new Version(1, 1, 2));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var name in new[] { "checkNow", "updateNow", "later" })
            {
                var button = (Button)typeof(PluginUpdatePage).GetField(name, flags).GetValue(update);
                typeof(Button).GetMethod("OnClick", flags).Invoke(button, new object[] { EventArgs.Empty });
            }
            Check(checksRequested == 1 && installs == 1 && laters == 1, "shared update buttons connected to existing actions");
            Check(Descendants(update).OfType<Label>().Any(x => x.Text.Contains("Test changes") && x.Text.Contains("9.0.0")), "shared update state includes version and notes");
            control.SetUpdateText("Prepared");
            Check(!((Button)typeof(PluginUpdatePage).GetField("checkNow", flags).GetValue(update)).Enabled, "prepared update disables repeated check");
        }
        var path = Write("host-settings.xml", System.Text.Encoding.UTF8.GetString(bytes));
        var original = File.ReadAllBytes(path);
        var plugin = new TimelineNoticeEditorPlugin();
        for (int cycle = 0; cycle < 2; cycle++)
        using (var page = new TabPage())
        using (var status = new Label())
        {
            plugin.InitPlugin(page, status, path, new UpdateService(new ReleaseTransport { Offline = true }));
            plugin.SetHostedLanguage("ko");
            Check(status.Text == Localization.Get("ko", "Started"), "public host entry updates status on reload");
            plugin.DeInitPlugin();
            Check(File.ReadAllBytes(path).SequenceEqual(original), "host reload preserves standalone settings bytes");
        }
    }
}
