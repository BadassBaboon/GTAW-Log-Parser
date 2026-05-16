using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Octokit;
using Serilog;

namespace Assistant.Controllers
{
    /// <summary>
    /// Downloads a new <c>GTAWAssistant</c> binary from the latest GitHub
    /// release, spawns a tiny cmd.exe one-liner that waits for the current
    /// process to exit, swaps the EXEs, and relaunches.
    /// </summary>
    public static class AutoUpdater
    {
        // Matches the artifact name produced by .github/workflows/release.yml
        // for framework-dependent x64 builds.
        private const string AssetNameFdd = "GTAWAssistant-fdd-win-x64.exe";
        private const string AssetNameSelfContained = "GTAWAssistant-selfcontained-win-x64.exe";

        /// <summary>
        /// Returns true on successful download + relaunch (the calling
        /// process should exit). Returns false if the asset can't be found
        /// or the download fails; the caller can fall back to opening the
        /// releases page in a browser.
        /// </summary>
        public static async Task<bool> TryUpdateAsync(Release release)
        {
            if (release == null)
                return false;

            try
            {
                ReleaseAsset? asset = PickAsset(release);
                if (asset == null)
                {
                    Log.Warning("AutoUpdater: no matching asset in release {Tag}", release.TagName);
                    return false;
                }

                string exePath = AppController.ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Log.Warning("AutoUpdater: current exe path is unavailable");
                    return false;
                }

                string newExePath = exePath + ".new";
                if (File.Exists(newExePath)) File.Delete(newExePath);

                Log.Information("AutoUpdater: downloading {Url} to {Path}", asset.BrowserDownloadUrl, newExePath);
                using (HttpClient http = new HttpClient())
                using (Stream remote = await http.GetStreamAsync(asset.BrowserDownloadUrl).ConfigureAwait(false))
                using (FileStream local = File.Create(newExePath))
                {
                    await remote.CopyToAsync(local).ConfigureAwait(false);
                }

                LaunchSwapScript(exePath, newExePath);

                // Caller exits the app — the script will pick up.
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AutoUpdater failed");
                return false;
            }
        }

        private static ReleaseAsset? PickAsset(Release release)
        {
            // Prefer fdd because it's faster to download; users who installed
            // the self-contained version manually can keep using the
            // browser-based update path.
            ReleaseAsset? fdd = release.Assets.FirstOrDefault(a =>
                a.Name.Equals(AssetNameFdd, StringComparison.OrdinalIgnoreCase));
            if (fdd != null) return fdd;

            return release.Assets.FirstOrDefault(a =>
                a.Name.Equals(AssetNameSelfContained, StringComparison.OrdinalIgnoreCase));
        }

        private static void LaunchSwapScript(string currentExe, string newExe)
        {
            // cmd.exe one-liner:
            //   1. Wait 2 seconds (give our process time to exit)
            //   2. Move the new exe over the old one (-y to overwrite)
            //   3. Start the new exe
            // /c keeps the cmd window invisible after the chain completes.
            string args = $"/c timeout /t 2 /nobreak >nul && move /y \"{newExe}\" \"{currentExe}\" >nul && start \"\" \"{currentExe}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }
}
