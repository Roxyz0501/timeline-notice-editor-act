using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TimelineNoticeEditor;

internal static partial class Program
{
    private sealed class ReleaseTransport : HttpMessageHandler
    {
        internal int Heads, Apis, Downloads;
        internal int ApiStatus = 429, HeadStatus = 302;
        internal string Tag, Redirect;
        internal byte[] Zip;
        internal string Manifest;
        internal bool Offline;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Offline) throw new HttpRequestException("offline");
            HttpResponseMessage response;
            if (request.Method == HttpMethod.Head)
            {
                Heads++; response = new HttpResponseMessage((HttpStatusCode)HeadStatus);
                response.Headers.Location = new Uri(Redirect ?? UpdateService.RepositoryUrl + "/releases/tag/" + Tag);
            }
            else if (request.RequestUri.Host == "api.github.com")
            {
                Apis++; response = new HttpResponseMessage((HttpStatusCode)ApiStatus);
                response.Content = new StringContent("[{\"tag_name\":\"" + Tag + "\",\"draft\":false,\"prerelease\":false,\"body\":\"Fixes\"}]");
            }
            else
            {
                Downloads++; response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Content = request.RequestUri.AbsolutePath.EndsWith(".zip") ? (HttpContent)new ByteArrayContent(Zip) : new StringContent(Manifest);
            }
            return Task.FromResult(response);
        }
    }

    private static void UpdateNetwork()
    {
        var version = typeof(PluginSettings).Assembly.GetName().Version;
        var tag = "v" + version.ToString(3);
        var same = new ReleaseTransport { Tag = tag };
        using (var service = new UpdateService(same))
            Check(service.CheckAsync(version, CancellationToken.None).Result.Status == UpdateCheckStatus.UpToDate && same.Heads == 1 && same.Apis == 0 && same.Downloads == 0, "same version: HEAD=1 API=0 downloads=0");
        var newer = new ReleaseTransport { Tag = tag, ApiStatus = 200 };
        using (var service = new UpdateService(newer))
        {
            var result = service.CheckAsync(new Version(0, 0, 0), CancellationToken.None).Result;
            Check(result.Status == UpdateCheckStatus.UpdateAvailable && result.Release.Notes == "Fixes" && newer.Apis == 1 && newer.Downloads == 0, "new version metadata: HEAD=1 API=1 download=0");
        }
        var zipPath = Path.Combine(artifacts, "network-package.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(typeof(PluginSettings).Assembly.Location, "TimelineNoticeEditor.dll");
            zip.CreateEntryFromFile(typeof(TimelineNoticeEditor.Updater.Program).Assembly.Location, "TimelineNoticeEditor.Updater.exe");
            zip.CreateEntry("README.md");
        }
        foreach (var status in new[] { 403, 429 })
        {
            var transport = new ReleaseTransport { Tag = tag, ApiStatus = status, Zip = File.ReadAllBytes(zipPath),
                Manifest = UpdatePackageVerifier.ComputeSha256(zipPath) + "  TimelineNoticeEditor-" + tag + ".zip" };
            using (var service = new UpdateService(transport))
            {
                var result = service.CheckAsync(new Version(0, 0, 0), CancellationToken.None).Result;
                Check(result.Status == UpdateCheckStatus.UpdateAvailable && result.Error == "CheckLimited" && transport.Apis == 1 && transport.Downloads == 0, "limited metadata preserves explicit update " + status);
                var package = service.DownloadAndVerifyAsync(result.Release, CancellationToken.None).Result;
                Check(File.Exists(package.StagedUpdaterPath) && transport.Downloads == 2 && transport.Apis == 1, "limited API verified ZIP and SHA download " + status);
                transport.Manifest = new string('0', 64) + "  TimelineNoticeEditor-" + tag + ".zip";
                Throws(() => service.DownloadAndVerifyAsync(result.Release, CancellationToken.None).GetAwaiter().GetResult(), "corrupt fallback package rejected " + status);
            }
        }
        foreach (var redirect in new[] { "https://github.com/other/repo/releases/tag/v2.0.0", UpdateService.RepositoryUrl + "/releases/tag/v2.0.0-beta", UpdateService.RepositoryUrl + "/releases/tag/v2.0.0?x=1", "http://github.com/Roxyz0501/timeline-notice-editor-act/releases/tag/v2.0.0" })
        {
            var transport = new ReleaseTransport { Redirect = redirect };
            using (var service = new UpdateService(transport))
                Check(service.CheckAsync(new Version(0, 0, 0), CancellationToken.None).Result.Status == UpdateCheckStatus.Failed && transport.Apis == 0, "unsafe latest redirect rejected");
        }
        foreach (var status in new[] { 403, 429 })
        using (var service = new UpdateService(new ReleaseTransport { Tag = tag, HeadStatus = status }))
            Check(service.CheckAsync(version, CancellationToken.None).Result.Error == "CheckLimited", "HEAD limit distinct " + status);
        using (var service = new UpdateService(new ReleaseTransport { Offline = true }))
            Check(service.CheckAsync(version, CancellationToken.None).Result.Error == "CheckFailed", "offline distinct from rate limit");
        foreach (var language in new[] { "en", "ja", "zh-CN", "ko" })
            Check(Localization.Get(language, "CheckLimited") != Localization.Get(language, "CheckFailed") && Localization.Get(language, "UpdateLimited") != "UpdateLimited", "localized rate limit " + language);
        Console.WriteLine("Update network measurement: same version HEAD=1/API=0/download=0; newer limited HEAD=1/API=1/download=2 on explicit prepare; retries=0.");
    }
}
