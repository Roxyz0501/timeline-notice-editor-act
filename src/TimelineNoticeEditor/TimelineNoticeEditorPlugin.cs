using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TimelineNoticeEditor
{
    public sealed class TimelineNoticeEditorPlugin : IActPluginV1
    {
        private UpdateService updates;
        private CancellationTokenSource cancellation;
        private PluginSettings settings;
        private SettingsControl control;
        private Label status;
        private string settingsPath;
        private SettingsSaveCoordinator settingsSaver;
        private volatile bool stopped = true;
        private bool checking, preparing;
        private int generation;
        private readonly object taskGate = new object();
        private readonly System.Collections.Generic.List<Task> operations = new System.Collections.Generic.List<Task>();
        private EventHandler checkHandler, installHandler, laterHandler;
        private UpdateCheckResult lastUpdate;

        public void InitPlugin(TabPage page, Label statusLabel)
        {
            InitPlugin(page, statusLabel, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Advanced Combat Tracker", "Config", "TimelineNoticeEditor.xml"), new UpdateService());
        }

        internal void InitPlugin(TabPage page, Label statusLabel, string path, UpdateService service)
        {
            if (!stopped) throw new InvalidOperationException("Plugin is already initialized.");
            generation++; stopped = false; checking = preparing = false; lastUpdate = null;
            updates = service; cancellation = new CancellationTokenSource();
            operations.Clear();
            status = statusLabel;
            settingsPath = path;
            settings = PluginSettings.Load(settingsPath); settings.Normalize();
            var initialSnapshot = settings.Serialize();
            byte[] initialDisk = null;
            try { if (File.Exists(settingsPath)) initialDisk = File.ReadAllBytes(settingsPath); } catch { }
            settingsSaver = new SettingsSaveCoordinator(settingsPath, () => settings.Serialize(), initialSnapshot, initialDisk);
            settings.InitializeLanguageIfMissing(CultureInfo.CurrentUICulture);
            settings.BackupDirectoryResolver = () => {
                try { return Path.Combine(Path.GetDirectoryName(ResolvePluginDllPath(ActGlobals.oFormActMain.ActPlugins, this)), "Backups", "Timeline"); }
                catch { throw new IOException("BackupUnavailable"); }
            };
            control = new SettingsControl(settings); page.Text = "Timeline Notice Editor"; page.Controls.Add(control);
            checkHandler = delegate { StartOperation(() => CheckUpdatesAsync(true)); };
            installHandler = (s, e) => StartOperation(InstallAsync);
            laterHandler = delegate { if (lastUpdate?.Release == null) return; settings.SkippedVersion = lastUpdate.Release.Version.ToString(); control.ShowUpdateSkipped(settings.SkippedVersion); };
            control.CheckUpdatesRequested += checkHandler;
            control.InstallUpdateRequested += installHandler;
            control.LaterRequested += laterHandler;
            status.Text = T("Started");
            if (settings.CheckUpdatesOnStartup) StartOperation(() => CheckUpdatesAsync(false));
        }
        private string T(string key, params object[] args) => Localization.Get(settings?.Language ?? "en", key, args);
        private void StartOperation(Func<Task> operation)
        {
            if (stopped) return;
            var task = operation();
            lock (taskGate) { operations.RemoveAll(x => x.IsCompleted); operations.Add(task); }
        }
        private async Task CheckUpdatesAsync(bool manual)
        {
            if (checking || preparing || stopped) return; checking = true; control.SetUpdateChecking();
            var run = generation;
            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version;
                var result = await updates.CheckAsync(current, cancellation.Token).ConfigureAwait(false); if (stopped) return;
                PostUi(run, () => { lastUpdate = result; control.ShowUpdateResult(result, current);
                    if (result.Status == UpdateCheckStatus.UpdateAvailable)
                    {
                        if (!manual && settings.SkippedVersion == result.Release.Version.ToString()) control.ShowUpdateSkipped(settings.SkippedVersion);
                        else { control.FocusUpdateTab(); status.Text = T("UpdateAvailable", current.ToString(3), result.Release.Version); }
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch { PostUi(run, () => control.SetUpdateText("CheckFailed")); }
            finally { checking = false; }
        }
        private async Task InstallAsync()
        {
            if (preparing || stopped || lastUpdate?.Status != UpdateCheckStatus.UpdateAvailable) return;
            preparing = true;
            var run = generation;
            control.SetUpdateText("Preparing");
            try
            {
                var package = await updates.DownloadAndVerifyAsync(lastUpdate.Release, cancellation.Token).ConfigureAwait(false); if (stopped) return;
                PostUi(run, () => { control.SetUpdateText("Prepared", package.StagedDllPath); status.Text = T("Prepared", package.StagedDllPath); });
            }
            catch { PostUi(run, () => { control.SetUpdateText("UpdateFailed", T("UpdateProblem")); status.Text = T("UpdateProblem"); }); }
            finally { preparing = false; }
        }
        private void PostUi(int run, Action action)
        {
            var target = control;
            if (stopped || run != generation || target == null || target.IsDisposed || !target.IsHandleCreated) return;
            try { target.BeginInvoke(new Action(() => { if (!stopped && run == generation && !target.IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
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
            if (stopped) return;
            stopped = true;
            if (control != null) { control.CheckUpdatesRequested -= checkHandler; control.InstallUpdateRequested -= installHandler; control.LaterRequested -= laterHandler; }
            cancellation.Cancel();
            Task[] pending; lock (taskGate) pending = operations.Where(x => !x.IsCompleted).ToArray();
            try { Task.WaitAll(pending); } catch (AggregateException) { }
            control?.Dispose();
            settingsSaver?.StopAndFlush();
            if (settingsSaver?.LastError != null) status.Text = T("SaveFailed"); else status.Text = T("Stopped");
            updates.Dispose(); cancellation.Dispose();
        }
    }
}
