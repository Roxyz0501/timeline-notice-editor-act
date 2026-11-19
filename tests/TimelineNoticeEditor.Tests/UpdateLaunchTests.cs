using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TimelineNoticeEditor;

internal static partial class Program
{
    private static void UpdateLaunch()
    {
        var dll = typeof(PluginSettings).Assembly.Location;
        var updater = typeof(TimelineNoticeEditor.Updater.Program).Assembly.Location;
        SemVersion version; SemVersion.TryParse(typeof(PluginSettings).Assembly.GetName().Version.ToString(3), out version);
        var target = Path.GetFullPath(Write("日本語 path/TimelineNoticeEditor.dll", "original"));
        var package = new PreparedUpdate { StagedDllPath = dll, StagedUpdaterPath = updater,
            DllSha256 = UpdatePackageVerifier.ComputeSha256(dll), UpdaterSha256 = UpdatePackageVerifier.ComputeSha256(updater),
            Release = new ReleaseInfo { Version = version } };
        var info = UpdateLauncher.CreateStartInfo(package, target, 123);
        Check(info.FileName == updater && !info.UseShellExecute && info.CreateNoWindow, "verified staged updater runs hidden");
        Check(info.Arguments.Contains("--target \"" + target + "\"") && info.Arguments.Contains("--original " + UpdatePackageVerifier.ComputeSha256(target)), "quoted Japanese registered path and original hash");
        using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = System.Reflection.Assembly.GetExecutingAssembly().Location, Arguments = "--wait-child",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden }))
        using (var helper = System.Diagnostics.Process.Start(UpdateLauncher.CreateStartInfo(package, target, child.Id)))
        {
            System.Threading.Thread.Sleep(250);
            Check(!helper.HasExited && File.ReadAllText(target) == "original", "updater waits without overwriting loaded target");
            Check(helper.WaitForExit(10000) && helper.ExitCode == 0, "updater exits after host exits");
            Check(UpdatePackageVerifier.VerifySha256(target, package.DllSha256) &&
                Directory.GetFiles(Path.GetDirectoryName(target), "*.backup-*").Any(x => File.ReadAllText(x) == "original"), "real helper replaces with backup after exit");
        }
        package.UpdaterSha256 = new string('0', 64);
        Throws(() => UpdateLauncher.CreateStartInfo(package, target, 123), "modified updater rejected before launch");
        foreach (var lang in new[] { "en", "ja", "zh-CN", "ko" })
        using (var control = new SettingsControl(new PluginSettings { Language = lang }))
        {
            var tabs = Descendants(control).OfType<TabControl>().Single();
            Check(tabs.GetType().FullName == "Roxyz.Act.UI.PluginTabs" && tabs.SizeMode == TabSizeMode.Normal, "shared natural width tabs " + lang);
            Check(Descendants(tabs.TabPages[2]).OfType<Label>().Any(x => x.Text.Contains("Roxyz0501")) &&
                Descendants(tabs.TabPages[2]).OfType<LinkLabel>().Any(x => x.Text.StartsWith("GitHub")), "author and repository on update tab " + lang);
            Check(Localization.Get(lang, "Prepared") != "Prepared" && Localization.Get(lang, "Author") != "Author" || lang == "en", "update localization " + lang);
        }
    }
}

