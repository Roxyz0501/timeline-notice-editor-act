using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TimelineNoticeEditor;

internal static partial class Program
{
    private sealed class CancelledTransport : HttpMessageHandler
    {
        internal int Requests, Finished;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            try { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); throw new Exception("unreachable"); }
            finally { Interlocked.Increment(ref Finished); }
        }
    }

    private static void Lifecycle()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var path = Path.Combine(artifacts, "lifecycle-settings.xml");
                var initial = new PluginSettings { Language = "en", TimelineFolder = artifacts, CheckUpdatesOnStartup = false };
                PluginSettings.SaveSerialized(path, initial.Serialize(), null);
                var bytes = File.ReadAllBytes(path);
                var plugin = new TimelineNoticeEditorPlugin();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                using (var page = new TabPage())
                using (var label = new Label())
                {
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        var transport = new CancelledTransport();
                        plugin.InitPlugin(page, label, path, new UpdateService(transport));
                        // A UI context that cannot be pumped while DeInitPlugin blocks.
                        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                        Task pending;
                        if (cycle == 0)
                        {
                            SemVersion version; SemVersion.TryParse("9.0.0", out version);
                            var release = new ReleaseInfo { Tag = "v9.0.0", Version = version };
                            foreach (var extension in new[] { "zip", "sha256" })
                            {
                                var name = "TimelineNoticeEditor-v9.0.0." + extension;
                                release.Assets.Add(new ReleaseAsset { Name = name, Size = 100, DownloadUrl = "https://github.com/Roxyz0501/timeline-notice-editor-act/releases/download/v9.0.0/" + name });
                            }
                            typeof(TimelineNoticeEditorPlugin).GetField("lastUpdate", flags).SetValue(plugin, new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = release });
                            pending = (Task)typeof(TimelineNoticeEditorPlugin).GetMethod("InstallAsync", flags).Invoke(plugin, null);
                        }
                        else pending = (Task)typeof(TimelineNoticeEditorPlugin).GetMethod("CheckUpdatesAsync", flags).Invoke(plugin, new object[] { true });
                        var tasks = (System.Collections.Generic.List<Task>)typeof(TimelineNoticeEditorPlugin).GetField("operations", flags).GetValue(plugin);
                        tasks.Add(pending);
                        Check(transport.Requests == 1 && !pending.IsCompleted, "mock network operation is in flight");
                        plugin.DeInitPlugin();
                        Check(pending.IsCompleted && transport.Finished == 1, "shutdown drains cancelled network operation without UI pumping");
                        plugin.DeInitPlugin();
                        Application.DoEvents();
                    }
                    Check(File.ReadAllBytes(path).SequenceEqual(bytes), "reload and unchanged shutdown preserve settings bytes");
                    Check(!File.Exists(path + ".bak"), "unchanged plugin lifecycle performs zero settings replacements");
                }
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Check(thread.Join(5000), "plugin shutdown and reload finish within five seconds");
        if (failure != null) throw failure;
    }
}
