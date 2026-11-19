using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TimelineNoticeEditor
{
    internal sealed class UpdateLimitException : IOException { }
    internal sealed partial class UpdateService
    {
        internal const string RepositoryUrl = "https://github.com/Roxyz0501/timeline-notice-editor-act";
        internal static bool TryStableTag(string tag, out SemVersion version)
        {
            return SemVersion.TryParse(tag, out version) && version.IsStable &&
                (tag == version.ToString() || tag == "v" + version.ToString());
        }

        private static async Task<string> ReadMetadataAsync(HttpContent content, CancellationToken token)
        {
            using (var input = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192]; int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("Metadata too large");
                    output.Write(buffer, 0, count);
                }
                return System.Text.Encoding.UTF8.GetString(output.ToArray());
            }
        }

        private async Task<UpdateCheckResult> CheckLatestAsync(Version current, CancellationToken token)
        {
            ReleaseInfo release;
            using (var request = new HttpRequestMessage(HttpMethod.Head, RepositoryUrl + "/releases/latest"))
            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                var status = (int)response.StatusCode;
                if (status == 403 || status == 429) throw new UpdateLimitException();
                if (status == 404) return new UpdateCheckResult { Status = UpdateCheckStatus.NoStableRelease };
                if (status != 301 && status != 302 && status != 303 && status != 307 && status != 308) throw new InvalidDataException("Invalid latest redirect");
                var location = response.Headers.Location;
                if (location == null) throw new InvalidDataException("Missing latest redirect");
                var uri = location.IsAbsoluteUri ? location : new Uri(new Uri(RepositoryUrl), location);
                var prefix = RepositoryUrl + "/releases/tag/";
                if (!uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected release repository");
                var tag = uri.AbsoluteUri.Substring(prefix.Length);
                SemVersion version;
                if (!TryStableTag(tag, out version)) throw new InvalidDataException("Invalid stable tag");
                release = new ReleaseInfo { Tag = tag, Version = version, OfficialFallback = true };
                foreach (var extension in new[] { ".zip", ".sha256" })
                {
                    var name = "TimelineNoticeEditor-v" + version + extension;
                    release.Assets.Add(new ReleaseAsset { Name = name, DownloadUrl = RepositoryUrl + "/releases/download/" + tag + "/" + name });
                }
            }
            SemVersion installed;
            if (current == null || !SemVersion.TryParse(current.ToString(3), out installed)) throw new InvalidDataException("Invalid installed version");
            if (release.Version.CompareTo(installed) <= 0) return new UpdateCheckResult { Status = UpdateCheckStatus.UpToDate, Release = release };
            var result = new UpdateCheckResult { Status = UpdateCheckStatus.UpdateAvailable, Release = release };
            // Metadata is optional; only the official latest redirect establishes the fallback tag.
            try
            {
                using (var response = await client.GetAsync(UpdateConfiguration.ReleasesApiUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429) { result.Error = "CheckLimited"; return result; }
                    response.EnsureSuccessStatusCode();
                    var json = await ReadMetadataAsync(response.Content, token).ConfigureAwait(false);
                    var match = ReleaseParser.ParseStableReleases(json).FirstOrDefault(x => x.Tag == release.Tag);
                    if (match != null) result.Release = match;
                }
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); result.Error = "CheckFailed"; }
            catch { result.Error = "CheckFailed"; }
            return result;
        }
    }
}
