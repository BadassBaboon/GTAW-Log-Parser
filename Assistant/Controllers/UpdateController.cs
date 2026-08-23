using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Octokit;
using Serilog;

namespace Assistant.Controllers
{
    public static class UpdateController
    {
        private const string AssistantAssetName = "GTAWAssistant.exe";
        private const string MiniAssetName = "ParserMini.exe";
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private sealed class UpdateFile
        {
            public string TargetPath { get; set; } = string.Empty;
            public string BackupPath { get; set; } = string.Empty;
            public string DownloadPath { get; set; } = string.Empty;
        }

        public static string GetRollbackDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "GTAW-Log-Parser", "rollback");
        }

        public static bool HasRollback()
        {
            try
            {
                string assistantBackup = GetBackupPath(AppController.ExecutablePath);
                return File.Exists(assistantBackup);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<(bool Success, string? Error)> TryInstallAsync(Release release)
        {
            try
            {
                if (release == null)
                    return (false, "No release information was provided.");

                string directory = AppController.StartupPath;
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return (false, "Application startup directory could not be found.");

                string rollbackDir = GetRollbackDirectory();
                EnsureWritableDirectory(directory);
                EnsureWritableDirectory(rollbackDir);

                List<UpdateFile> files = new List<UpdateFile>();

                // Find GTAWAssistant asset
                ReleaseAsset? assistantAsset = FindAsset(release, AssistantAssetName)
                    ?? FindAsset(release, "GTAWAssistant-fdd-win-x64.exe")
                    ?? FindAsset(release, "GTAWAssistant-selfcontained-win-x64.exe");

                if (assistantAsset == null)
                    return (false, $"The release '{release.TagName}' does not include a compatible GTAWAssistant executable.");

                UpdateFile assistantFile = await DownloadAssetAsync(release, assistantAsset, AppController.ExecutablePath).ConfigureAwait(false);
                files.Add(assistantFile);

                // Optional: Mini Parser asset if Mini exists in directory
                string miniPath = Path.Combine(directory, MiniAssetName);
                if (File.Exists(miniPath))
                {
                    ReleaseAsset? miniAsset = FindAsset(release, MiniAssetName)
                        ?? FindAsset(release, "ParserMini-fdd-win-x64.exe")
                        ?? FindAsset(release, "ParserMini-selfcontained-win-x64.exe");

                    if (miniAsset != null)
                    {
                        UpdateFile miniFile = await DownloadAssetAsync(release, miniAsset, miniPath).ConfigureAwait(false);
                        files.Add(miniFile);
                    }
                }

                StartReplacementScript(files, AppController.ExecutablePath, Process.GetCurrentProcess().Id, false);
                return (true, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to install update");
                return (false, ex.Message);
            }
        }

        public static bool TryRestorePreviousVersion(out string? error)
        {
            error = null;
            try
            {
                string assistantBackup = GetBackupPath(AppController.ExecutablePath);
                if (!File.Exists(assistantBackup))
                {
                    error = "No previous GTAWAssistant version backup is available.";
                    return false;
                }

                EnsureWritableDirectory(GetRollbackDirectory());

                List<UpdateFile> files = new List<UpdateFile>
                {
                    new UpdateFile { TargetPath = AppController.ExecutablePath, BackupPath = assistantBackup }
                };

                string miniPath = Path.Combine(AppController.StartupPath, MiniAssetName);
                string miniBackup = GetBackupPath(miniPath);
                if (File.Exists(miniPath) && File.Exists(miniBackup))
                {
                    files.Add(new UpdateFile { TargetPath = miniPath, BackupPath = miniBackup });
                }

                StartReplacementScript(files, AppController.ExecutablePath, Process.GetCurrentProcess().Id, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restore previous version");
                error = ex.Message;
                return false;
            }
        }

        public static bool IsVersionNewer(string available, string installed) =>
            GTAWParser.Shared.VersionHelper.IsVersionNewer(available, installed);

        public static bool TryParseVersion(string? value, out Version? version) =>
            GTAWParser.Shared.VersionHelper.TryParseVersion(value, out version);

        private static ReleaseAsset? FindAsset(Release release, string name)
        {
            return release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<UpdateFile> DownloadAssetAsync(Release release, ReleaseAsset asset, string targetPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "GTAW-Log-Parser", "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string downloadPath = Path.Combine(tempDir, asset.Name);

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl))
            {
                request.Headers.UserAgent.ParseAdd(AppController.ProductHeader);
                using (HttpResponseMessage response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream fs = new FileStream(downloadPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fs).ConfigureAwait(false);
                    }
                }
            }

            // Optional SHA-256 validation if digest is provided in release
            string? expectedHash = await TryGetAssetDigestAsync(release, asset.Name).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                string downloadedHash = ComputeSha256(downloadPath);
                if (!string.Equals(expectedHash, downloadedHash, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(downloadPath); } catch { }
                    throw new IOException($"Downloaded asset '{asset.Name}' failed SHA-256 integrity check. (Expected: {expectedHash}, Actual: {downloadedHash})");
                }
                Log.Information("SHA-256 digest verified successfully for {Asset}", asset.Name);
            }

            return new UpdateFile
            {
                TargetPath = targetPath,
                BackupPath = GetBackupPath(targetPath),
                DownloadPath = downloadPath
            };
        }

        private static Task<string?> TryGetAssetDigestAsync(Release release, string assetName)
        {
            try
            {
                // Check if release body contains a SHA-256 line like: `<sha256> *GTAWAssistant.exe` or `GTAWAssistant.exe: <sha256>`
                if (!string.IsNullOrEmpty(release.Body))
                {
                    string[] lines = release.Body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        if (line.Contains(assetName, StringComparison.OrdinalIgnoreCase))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(line, @"\b[a-fA-F0-9]{64}\b");
                            if (match.Success)
                                return Task.FromResult<string?>(match.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not resolve asset digest from release body");
            }

            return Task.FromResult<string?>(null);
        }

        private static void EnsureWritableDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            string probePath = Path.Combine(directory, ".gtaw-update-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(probePath, System.IO.FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }
            }
            finally
            {
                if (File.Exists(probePath))
                {
                    try { File.Delete(probePath); } catch { }
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string GetBackupPath(string targetPath)
        {
            string name = Path.GetFileNameWithoutExtension(targetPath);
            string extension = Path.GetExtension(targetPath);
            return Path.Combine(GetRollbackDirectory(), name + ".previous" + extension);
        }

        private static void StartReplacementScript(IEnumerable<UpdateFile> files, string applicationPath, int processId, bool rollback)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), "GTAW-Log-Parser-update-" + Guid.NewGuid().ToString("N") + ".cmd");
            StringBuilder script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("chcp 65001 >nul");
            script.AppendLine("setlocal");
            script.AppendLine(":wait_for_app");
            script.AppendLine($"tasklist /FI \"PID eq {processId}\" | find \"{processId}\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto wait_for_app");
            script.AppendLine(")");

            foreach (UpdateFile file in files)
            {
                if (rollback)
                {
                    string temporary = file.TargetPath + ".restore-temp";
                    script.AppendLine($"copy /y \"{file.TargetPath}\" \"{temporary}\" >nul || goto failed");
                    script.AppendLine($"copy /y \"{file.BackupPath}\" \"{file.TargetPath}\" >nul || goto failed");
                    script.AppendLine($"copy /y \"{temporary}\" \"{file.BackupPath}\" >nul || goto failed");
                    script.AppendLine($"del /q \"{temporary}\" >nul 2>&1");
                }
                else
                {
                    script.AppendLine($"copy /y \"{file.TargetPath}\" \"{file.BackupPath}\" >nul || goto failed");
                    script.AppendLine($"copy /y \"{file.DownloadPath}\" \"{file.TargetPath}\" >nul || goto failed");
                    script.AppendLine($"del /q \"{file.DownloadPath}\" >nul 2>&1");
                }
            }

            script.AppendLine($"start \"\" \"{applicationPath}\"");
            script.AppendLine("del \"%~f0\" >nul 2>&1");
            script.AppendLine("exit /b");
            script.AppendLine(":failed");
            script.AppendLine($"start \"\" \"{applicationPath}\"");
            script.AppendLine("del \"%~f0\" >nul 2>&1");
            File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(false));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }
}
