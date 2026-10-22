using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

internal static partial class Program
{
    private static void SettingsPersistence()
    {
        Check(TimelineNoticeEditor.SettingsSaveCoordinator.SaveInterval == TimeSpan.FromSeconds(60), "settings autosave uses one-minute fixed interval");
        var root = Path.Combine(artifacts, "settings"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.xml");
        var initial = new byte[] { 1 };
        var current = initial;
        var writes = 0; byte[] saved = null;
        using (var coordinator = new TimelineNoticeEditor.SettingsSaveCoordinator(path, () => current, initial, null,
            TimeSpan.FromMilliseconds(250), (p, bytes, expected) => { writes++; saved = (byte[])bytes.Clone(); }))
        {
            Thread.Sleep(350);
            Check(writes == 0, "unchanged settings cause zero writes");
            current = new byte[] { 2 }; current = new byte[] { 3 };
            Thread.Sleep(350);
            Check(writes == 1 && saved.SequenceEqual(new byte[] { 3 }), "rapid setting changes aggregate to latest snapshot");
            current = new byte[] { 1 };
            Thread.Sleep(350);
            Check(writes == 2 && saved.SequenceEqual(initial), "revert to saved bytes persists only when prior state differed");
        }

        var failOnce = true; var retained = new byte[] { 8 }; current = new byte[] { 9 };
        using (var coordinator = new TimelineNoticeEditor.SettingsSaveCoordinator(path, () => current, retained, retained,
            TimeSpan.FromMinutes(2), (p, bytes, expected) => { if (failOnce) { failOnce = false; throw new IOException("simulated"); } File.WriteAllBytes(p, bytes); }))
        {
            coordinator.SaveIfChanged();
            Check(coordinator.HasUnsavedChanges && coordinator.LastError != null && !File.Exists(path), "failed save leaves dirty state and creates no partial file");
            current = new byte[] { 10 }; coordinator.SaveIfChanged();
            Check(File.ReadAllBytes(path).SequenceEqual(current) && !coordinator.HasUnsavedChanges, "next scheduled opportunity saves latest state after failure");
        }

        var started = new ManualResetEvent(false); var release = new ManualResetEvent(false); current = new byte[] { 11 };
        var blocked = new TimelineNoticeEditor.SettingsSaveCoordinator(path, () => current, new byte[] { 10 }, new byte[] { 10 },
            TimeSpan.FromMinutes(2), (p, bytes, expected) => { started.Set(); release.WaitOne(); File.WriteAllBytes(p, bytes); });
        var saving = Task.Run(() => blocked.SaveIfChanged()); Check(started.WaitOne(2000), "test save enters writer");
        current = new byte[] { 12 };
        var stopping = Task.Run(() => blocked.StopAndFlush()); Thread.Sleep(50);
        Check(!stopping.IsCompleted, "shutdown waits for in-flight settings save");
        release.Set(); Check(stopping.Wait(2000), "shutdown completes after in-flight save drains");
        Check(File.ReadAllBytes(path).SequenceEqual(new byte[] { 12 }), "shutdown flush captures latest settings");
        blocked.Dispose(); started.Dispose(); release.Dispose();
        Check(!Directory.GetFiles(root, "*.tmp").Any(), "settings save leaves no temporary files");
    }
}
