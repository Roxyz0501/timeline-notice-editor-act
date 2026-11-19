# Preview resource limits (v1.1.2)

- The editor accounts for up to 256 MiB of owned raster buffers: decoded sources, rendered images, the GDI canvas copy, and temporary decode/render reservations. Source decoding reserves room for both the codec frame and a detached copy; metadata is checked before requesting a fully cached frame. A source is detached so it cannot keep the encoded file stream in the long-lived cache.
- This is a logical raster budget, not a promise about total process RSS or GPU usage. Codec/driver allocations, object metadata, and buffers awaiting garbage collection are outside that counter. Encoded input is separately limited to 20 MiB (a growing HTTPS buffer can have up to 32 MiB capacity). No forced GC is used.
- Only one image load/decode, including its completed result awaiting UI consumption, exists at a time. Pending selections carry references/flags only. Superseded hidden requests are cancelled. No image-bearing UI callbacks accumulate. HTTPS images still require an explicit click.
- Visible desktop previews and the selected canvas take priority. Hidden entries use LRU eviction: at most four entries and 8 MiB combined source/render data. Memory pressure may evict them earlier. Eviction releases images and closes hidden windows; active window handles are retained. A cache hit preserves its existing handle. An evicted remote image requires another explicit HTTPS load.
- Admission failure preserves already-visible previews and reports the limit in all four languages. After turning other previews off, reselect/re-enable the desired notice. Very large images may need a smaller source image. Original image files and XML coordinates are never reduced or rewritten automatically.
- Rendering reuses the current source revision/scale/placeholder text. Scale input changes the XML edit model immediately; a work-only 80 ms UI timer renders the latest value once per tick. During an image decode, rendering waits for its reserved memory to be released. Idle editors have no preview timer activity. Settings persistence remains its separate fixed 60-second schedule.
- Canvas conversion copies premultiplied pixels directly into a single GDI bitmap. It no longer encodes a PNG, decodes it through GDI, and clones the bitmap. Position changes only move existing images.

## Measured synthetic checks

24 notices referencing a 64 x 64 BGRA image, with a test raster budget of 262,144 bytes:

| Scenario | Observed result |
| --- | --- |
| Sequential selection | Peak retained raster counter: 199,520 bytes; at most 4 hidden entries plus selected entry |
| 30 identical render requests | 0 new renders; 0 new canvas copies |
| 20 scale inputs before one work tick | 1 render and 1 canvas copy; final scale retained |
| 20 position inputs | 0 renders and 0 XML writes |
| Rapidly select 24 notices | At most 2 loads: the initial in-flight load and final selection |
| Dispose | 0 owned raster bytes and no pending preview work |

Counters measure owned buffer sizes and calls, not physical SSD writes, GC pause times, or process working set. Tests also cover a denied source, exact budget admission/release, direct-copy colour/alpha, and (in the CI UI suite) preserving two active previews while refusing a third, then reclaiming hidden memory to admit it. No stress test allocates full-size 32-megapixel images.

XML remains dirty-only and explicitly saved through the existing backup path. Position/scale interaction does not save XML. No backup history is deleted automatically; users may inspect the plugin's Backups/Timeline folder and archive old backups themselves.
