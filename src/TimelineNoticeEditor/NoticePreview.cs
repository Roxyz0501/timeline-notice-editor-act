using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace TimelineNoticeEditor
{
    internal sealed class ImageResolver
    {
        private readonly string timeline, extra;
        internal ImageResolver(string timelinePath, string extraFolder) { timeline = Path.GetDirectoryName(timelinePath); extra = extraFolder; }
        internal string Resolve(string reference, bool icon)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            Uri uri;
            if (Uri.TryCreate(reference, UriKind.Absolute, out uri) && !uri.IsFile) return null;
            if (reference.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            if (Path.IsPathRooted(reference)) return System.IO.File.Exists(reference) ? Path.GetFullPath(reference) : null;
            var resource = new DirectoryInfo(timeline);
            while (resource != null && !Directory.Exists(Path.Combine(resource.FullName, "images")) && !Directory.Exists(Path.Combine(resource.FullName, "icon"))) resource = resource.Parent;
            var roots = new List<string> { timeline };
            if (resource != null) roots.Add(Path.Combine(resource.FullName, icon ? "icon" : "images"));
            if (!string.IsNullOrWhiteSpace(extra)) roots.Add(extra);
            foreach (var root in roots.Where(Directory.Exists))
            {
                var path = Path.GetFullPath(Path.Combine(root, reference));
                if (System.IO.File.Exists(path)) return path;
                if (Path.GetFileName(reference) != reference) continue;
                // Hojoring resources can be nested (for example Action icons/16_Dancer).
                var matches = Find(root, reference).Take(2).ToArray();
                if (matches.Length > 1) throw new InvalidDataException("AmbiguousImage");
                if (matches.Length == 1) return matches[0];
            }
            return null;
        }
        private static IEnumerable<string> Find(string root, string filename)
        {
            foreach (var file in Directory.EnumerateFiles(root)) if (string.Equals(Path.GetFileName(file), filename, StringComparison.OrdinalIgnoreCase)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(root))
            {
                if ((System.IO.File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var file in Find(child, filename)) yield return file;
            }
        }
        internal async Task<Tuple<BitmapSource, string>> LoadAsync(string reference, bool icon, bool allowRemote, CancellationToken token)
        {
            Uri uri;
            if (Uri.TryCreate(reference, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            {
                if (!allowRemote) throw new InvalidOperationException("RemoteConsent");
                if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("HttpsOnly");
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) })
                using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new InvalidDataException("HttpsOnly");
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength > 20 * 1024 * 1024) throw new InvalidDataException("ImageTooLarge");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[81920]; int n;
                        while ((n = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0) { output.Write(buffer, 0, n); if (output.Length > 20 * 1024 * 1024) throw new InvalidDataException("ImageTooLarge"); }
                        return Tuple.Create(Decode(output.ToArray()), reference);
                    }
                }
            }
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested(); var path = Resolve(reference, icon);
                if (path == null) return Tuple.Create<BitmapSource, string>(null, reference);
                if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("ImageTooLarge");
                return Tuple.Create(Decode(System.IO.File.ReadAllBytes(path)), path);
            }, token).ConfigureAwait(false);
        }
        private static BitmapSource Decode(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            {
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 32 * 1024 * 1024) throw new InvalidDataException("ImageTooLarge");
                frame.Freeze(); return frame;
            }
        }
    }

    internal static class NoticePreview
    {
        internal static BitmapSource Render(Notice notice, BitmapSource image, VisualSettings config, double scale, string missing)
        {
            var visual = new DrawingVisual();
            double width = image?.PixelWidth ?? 280, height = image?.PixelHeight ?? 80;
            if (!notice.IsImage)
            {
                var style = config?.Style(notice);
                var factor = config == null ? 1 : config.Value("OverlayScale", 1);
                factor = Math.Max(0.1, Math.Min(5, factor));
                var fontSize = Math.Max(6, Math.Min(200, Notice.Number((string)style?.Element("Font")?.Attribute("Size"), 32) * Notice.Number(notice.Get("font-scale"), 1))) * factor;
                var iconSize = Math.Max(8, Math.Min(512, Notice.Number((string)style?.Element("IconSize"), 32))) * factor;
                var family = (string)style?.Element("Font")?.Attribute("FontFamily") ?? "Yu Gothic UI";
                var text = new FormattedText(notice.Get("text"), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(family), FontStyles.Normal, (string)style?.Element("Font")?.Attribute("Weight") == "Bold" ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), fontSize, ColorBrush((string)style?.Element("Color"), Colors.White), 1);
                width = Math.Max(80, Math.Min(4000, config?.Value("NoticeWidth", 404) ?? 404));
                var contentHeight = Math.Max(image == null ? 0 : iconSize, text.Height);
                height = Math.Max(48, 32 * factor + contentHeight);
                using (var dc = visual.RenderOpen())
                {
                    double x = 16 * factor, y = 16 * factor;
                    if (image != null) { dc.DrawImage(image, new Rect(x, y + (contentHeight - iconSize) / 2, iconSize, iconSize)); x += iconSize + 5 * factor; }
                    var geometry = text.BuildGeometry(new Point(x, y + (contentHeight - text.Height) / 2));
                    dc.DrawGeometry(ColorBrush((string)style?.Element("Color"), Colors.White), new Pen(ColorBrush((string)style?.Element("OutlineColor"), Colors.Black), 1.5), geometry);
                    if (notice.Get("duration-visible") != "false")
                    {
                        var duration = new FormattedText(notice.Get("duration") == "" ? "5.0" : notice.Get("duration"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), fontSize, ColorBrush((string)style?.Element("Color"), Colors.White), 1);
                        dc.DrawText(duration, new Point(Math.Max(x + text.Width + 10, width - duration.Width - 16), y));
                    }
                }
            }
            else
            {
                width = width * scale + 6; height = height * scale + 6;
                if (width > 8000 || height > 8000 || width * height > 32 * 1024 * 1024) throw new InvalidDataException("ImageTooLarge");
                using (var dc = visual.RenderOpen())
                {
                    if (image != null) dc.DrawImage(image, new Rect(3, 3, width - 6, height - 6));
                    else { dc.DrawRectangle(Brushes.DimGray, null, new Rect(0, 0, width, height)); dc.DrawText(new FormattedText(missing, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Yu Gothic UI"), 14, Brushes.White, 1), new Point(8, 8)); }
                }
            }
            var result = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)), 96, 96, PixelFormats.Pbgra32);
            result.Render(visual); result.Freeze(); return result;
        }
        private static Brush ColorBrush(string text, Color fallback) { try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(text)); } catch { return new SolidColorBrush(fallback); } }
    }
}
