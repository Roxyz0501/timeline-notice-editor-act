using System;
using System.Windows.Media.Imaging;

namespace TimelineNoticeEditor
{
    // Accounts for owned raster buffers, including allocations in flight. Codec/driver
    // internals and memory awaiting GC are not a process working-set guarantee.
    internal sealed class PreviewBudget
    {
        internal const long DefaultLimit = 256L * 1024 * 1024;
        internal long Limit { get; }
        internal long Used { get; private set; }
        internal long Peak { get; private set; }
        internal long Available => Limit - Used;
        internal PreviewBudget(long limit = DefaultLimit) { Limit = limit; }
        internal Lease Reserve(long bytes)
        {
            if (bytes < 0 || bytes > Available) return null;
            Used += bytes; Peak = Math.Max(Peak, Used);
            return new Lease(this, bytes);
        }
        internal static long Bytes(BitmapSource image) => image == null ? 0 : checked(((long)image.PixelWidth * image.Format.BitsPerPixel + 7) / 8 * image.PixelHeight);
        internal sealed class Lease : IDisposable
        {
            private PreviewBudget owner;
            internal long Bytes { get; private set; }
            internal Lease(PreviewBudget owner, long bytes) { this.owner = owner; Bytes = bytes; }
            internal void Shrink(long bytes)
            {
                if (bytes < 0 || bytes > Bytes) throw new ArgumentOutOfRangeException(nameof(bytes));
                owner.Used -= Bytes - bytes; Bytes = bytes;
            }
            public void Dispose() { if (owner == null) return; owner.Used -= Bytes; owner = null; Bytes = 0; }
        }
    }
}
