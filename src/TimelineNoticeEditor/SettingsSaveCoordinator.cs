using System;
using System.Linq;
using System.Threading;

namespace TimelineNoticeEditor
{
    internal sealed class SettingsSaveCoordinator : IDisposable
    {
        internal static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(60);
        private readonly object gate = new object();
        private readonly string path;
        private readonly Func<byte[]> capture;
        private readonly Action<string, byte[], byte[]> writer;
        private readonly Timer timer;
        private byte[] savedSnapshot;
        private byte[] expectedDisk;
        private bool stopped;
        private bool shutdownStarted;
        private bool shutdownComplete;
        private Exception lastError;

        internal SettingsSaveCoordinator(string path, Func<byte[]> capture, byte[] initialSnapshot, byte[] initialDisk,
            TimeSpan? interval = null, Action<string, byte[], byte[]> writer = null)
        {
            this.path = path;
            this.capture = capture;
            this.writer = writer ?? PluginSettings.SaveSerialized;
            savedSnapshot = initialSnapshot == null ? null : (byte[])initialSnapshot.Clone();
            expectedDisk = initialDisk == null ? null : (byte[])initialDisk.Clone();
            var period = interval ?? SaveInterval;
            timer = new Timer(_ => SaveIfChanged(), null, period, period);
        }

        internal Exception LastError { get { lock (gate) return lastError; } }
        internal bool HasUnsavedChanges { get { lock (gate) return IsChanged(capture()); } }

        internal void SaveIfChanged()
        {
            lock (gate)
            {
                if (stopped || shutdownStarted) return;
                CaptureAndSave();
            }
        }

        private bool IsChanged(byte[] current) => savedSnapshot == null || !current.SequenceEqual(savedSnapshot);

        private void CaptureAndSave()
        {
            try { SaveCaptured(capture()); }
            catch (Exception ex) { lastError = ex; }
        }

        private void SaveCaptured(byte[] current)
        {
            if (!IsChanged(current)) return;
            try
            {
                writer(path, current, expectedDisk);
                savedSnapshot = (byte[])current.Clone();
                expectedDisk = (byte[])current.Clone();
                lastError = null;
            }
            catch (Exception ex) { lastError = ex; }
        }

        internal void StopAndFlush()
        {
            bool owner;
            lock (gate)
            {
                owner = !shutdownStarted;
                if (owner) shutdownStarted = true;
            }
            if (!owner) { lock (gate) while (!shutdownComplete) Monitor.Wait(gate); return; }
            try
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
                using (var drained = new ManualResetEvent(false)) { timer.Dispose(drained); drained.WaitOne(); }
                lock (gate) { CaptureAndSave(); stopped = true; }
            }
            finally { lock (gate) { shutdownComplete = true; Monitor.PulseAll(gate); } }
        }

        public void Dispose() => StopAndFlush();
    }
}
