using System.Linq;

namespace TimelineNoticeEditor
{
    internal static class UpdateConfiguration
    {
        public const string RepositoryOwner = "Roxyz0501";
        public const string RepositoryName = "timeline-notice-editor-act";
        public const string PluginAssemblyName = "TimelineNoticeEditor";
        public const string UpdaterAssemblyName = "TimelineNoticeEditor.Updater";
        public const string PluginFileName = "TimelineNoticeEditor.dll";
        public const string UpdaterFileName = "TimelineNoticeEditor.Updater.exe";

        public static bool IsConfigured => IsSafeSegment(RepositoryOwner) && IsSafeSegment(RepositoryName);

        public static string ReleasesApiUrl => "https://api.github.com/repos/" + RepositoryOwner + "/" + RepositoryName + "/releases?per_page=10";

        private static bool IsSafeSegment(string value) => !string.IsNullOrWhiteSpace(value) &&
            value.All(x => char.IsLetterOrDigit(x) || x == '-' || x == '_' || x == '.');
    }
}
