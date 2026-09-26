using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace TimelineNoticeEditor
{
    public sealed class TimelineNoticeEditorPlugin : IActPluginV1
    {
        private readonly UpdateService updates = new UpdateService();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private PluginSettings settings;
        private SettingsControl control;
        private Label status;
        private string settingsPath;
        private bool stopped, checking, preparing;
        private UpdateCheckResult lastUpdate;

        public void InitPlugin(TabPage page, Label statusLabel)
        {
            status = statusLabel;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "TimelineNoticeEditor.xml");
            settings = PluginSettings.Load(settingsPath); settings.InitializeLanguageIfMissing(CultureInfo.CurrentUICulture); settings.Normalize();
            settings.BackupDirectoryResolver = () => {
                try { return Path.Combine(Path.GetDirectoryName(ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this)), "Backups", "Timeline"); }
                catch { throw new IOException("BackupUnavailable"); }
            };
            control = new SettingsControl(settings); page.Text = "Timeline Notice Editor"; page.Controls.Add(control);
            control.SettingsChanged += SettingsChanged;

            control.CheckUpdatesRequested += delegate { CheckUpdates(true); };
            control.InstallUpdateRequested += Install;
            control.LaterRequested += delegate { if (lastUpdate?.Release == null) return; settings.SkippedVersion = lastUpdate.Release.Version.ToString(); Save(); control.ShowUpdateSkipped(settings.SkippedVersion); };
            Save();
            status.Text = T("Started");
            if (settings.CheckUpdatesOnStartup) CheckUpdates(false);
        }
        private string T(string key, params object[] args) => Localization.Get(settings?.Language ?? "en", key, args);
        private void Save() { try { settings.Save(settingsPath); } catch { status.Text = T("SaveFailed"); } }
        private void SettingsChanged(object source, EventArgs e) { Save(); }
        private async void CheckUpdates(bool manual)
        {
            if (checking || preparing || stopped) return; checking = true; control.SetUpdateChecking();
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version;
                var result = await updates.CheckAsync(current, cancellation.Token); if (stopped) return;
                lastUpdate = result; control.ShowUpdateResult(result, current);
                if (result.Status == UpdateCheckStatus.UpdateAvailable)
                {
                    if (!manual && settings.SkippedVersion == result.Release.Version.ToString()) control.ShowUpdateSkipped(settings.SkippedVersion);
                    else { control.FocusUpdateTab(); status.Text = T("UpdateAvailable", current.ToString(3), result.Release.Version); }
                }
            }
            finally { checking = false; }
        }
        private async void Install(object source, EventArgs e)
        {
            if (preparing || stopped || lastUpdate?.Status != UpdateCheckStatus.UpdateAvailable) return;
            preparing = true; control.SetUpdateText("Preparing");
            try
            {
                var target = ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this);
                var package = await updates.DownloadAndVerifyAsync(lastUpdate.Release, cancellation.Token); if (stopped) return;
                updates.LaunchUpdater(package, target); control.SetUpdateText("Prepared"); status.Text = T("Prepared");
            }
            catch { if (!stopped) { control.SetUpdateText("UpdateFailed", T("UpdateProblem")); status.Text = T("UpdateProblem"); } }
            finally { preparing = false; }
        }
        internal static string ResolvePluginDllPath(IEnumerable<ActPluginData> plugins, IActPluginV1 instance)
        {
            var registration = plugins?.FirstOrDefault(x => x != null && ReferenceEquals(x.pluginObj, instance));
            return NormalizePluginPath(registration?.pluginFile?.FullName);
        }
        internal static string NormalizePluginPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("PluginPathUnavailable");
            Uri uri; if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.IsFile) value = uri.LocalPath;
            if (!Path.IsPathRooted(value)) throw new InvalidOperationException("PluginPathUnavailable");
            var path = Path.GetFullPath(value);
            if (!string.Equals(Path.GetFileName(path), UpdateConfiguration.PluginFileName, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidOperationException("PluginPathUnavailable");
            return path;
        }
        public void DeInitPlugin()
        {
            stopped = true; cancellation.Cancel(); Save(); control?.Dispose(); updates.Dispose(); status.Text = T("Stopped");
        }
    }
}
