using System;
using System.Diagnostics;
using System.IO;

namespace TimelineNoticeEditor
{
    internal static class UpdateLauncher
    {
        internal static ProcessStartInfo CreateStartInfo(PreparedUpdate package, string target, int processId)
        {
            if (package == null || processId <= 0) throw new InvalidDataException("InvalidPackage");
            target = TimelineNoticeEditorPlugin.NormalizePluginPath(target);
            UpdatePackageVerifier.ValidatePluginAssembly(package.StagedDllPath, package.Release.Version);
            UpdatePackageVerifier.ValidateUpdaterAssembly(package.StagedUpdaterPath, package.Release.Version);
            if (!UpdatePackageVerifier.VerifySha256(package.StagedDllPath, package.DllSha256) ||
                !UpdatePackageVerifier.VerifySha256(package.StagedUpdaterPath, package.UpdaterSha256))
                throw new InvalidDataException("HashFailed");
            return new ProcessStartInfo {
                FileName = Path.GetFullPath(package.StagedUpdaterPath),
                Arguments = "--wait-pid " + processId + " --source " + Quote(package.StagedDllPath) +
                    " --target " + Quote(target) + " --sha256 " + package.DllSha256 +
                    " --version " + package.Release.Version + " --original " + UpdatePackageVerifier.ComputeSha256(target),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
        }
        private static string Quote(string path)
        {
            path = Path.GetFullPath(path);
            if (path.IndexOf('"') >= 0) throw new InvalidDataException("Invalid path");
            return "\"" + path + "\"";
        }
    }
}
