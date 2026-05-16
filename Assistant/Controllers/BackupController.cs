using System;
using System.IO;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using Assistant.Utilities;
using Assistant.Localization;
using System.Text.RegularExpressions;

namespace Assistant.Controllers
{
    public static class BackupController
    {
        private const int GameClosedCheckTime = 10;

        private static CancellationTokenSource _cts;
        private static Task _backupTask;
        private static Task _intervalTask;

        private static string directoryPath;
        private static string backupPath;
        private static bool isGameRunning;

        private static bool _quitting;
        public static bool Quitting
        {
            get => _quitting;
            set
            {
                _quitting = value;
                if (value)
                    AbortAll();
            }
        }

        /// <summary>
        /// Displays a message box on the main UI thread.
        /// </summary>
        private static void DisplayBackupResultMessage(string text, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            Application.Current?.Dispatcher?.Invoke(() => MessageBox.Show(text, title, buttons, image));
        }

        /// <summary>
        /// Starts the backup workers if they are enabled. Safe to call repeatedly:
        /// any previous workers are cancelled first.
        /// </summary>
        public static void Initialize()
        {
            directoryPath = Properties.Settings.Default.DirectoryPath;
            backupPath = Properties.Settings.Default.BackupPath;

            bool enableAutomaticBackup = Properties.Settings.Default.BackupChatLogAutomatically;
            bool enableIntervalBackup = Properties.Settings.Default.EnableIntervalBackup;

            if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
                return;
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(Path.Combine(directoryPath, "client_resources")))
                return;
            if (Quitting)
                return;

            // Cancel any previous run; old workers see the cancellation at their next await.
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            if (enableAutomaticBackup)
                _backupTask = Task.Run(() => BackupWorkerAsync(ct), ct);

            if (enableIntervalBackup)
                _intervalTask = Task.Run(() => IntervalWorkerAsync(ct), ct);
        }

        /// <summary>
        /// Signals both workers to stop at their next await point.
        /// </summary>
        public static void AbortAll()
        {
            try { _cts?.Cancel(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AbortAll failed: {ex}"); }
        }

        private static async Task BackupWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Process[] processes = Process.GetProcesses()
                        .Where(p => AppController.ProcessNames.Contains(p.ProcessName))
                        .ToArray();

                    if (!isGameRunning && processes.Length != 0)
                        isGameRunning = true;
                    else if (isGameRunning && processes.Length == 0)
                    {
                        isGameRunning = false;
                        ParseThenSaveToFile(true);
                    }

                    await Task.Delay(GameClosedCheckTime * 1000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation path.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BackupWorker failed: {ex}");
            }
        }

        private static async Task IntervalWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int intervalMinutes = Properties.Settings.Default.IntervalTime;

                    if (isGameRunning && File.Exists(Path.Combine(directoryPath, AppController.LogLocation)))
                        ParseThenSaveToFile();

                    await Task.Delay(intervalMinutes * 60_000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation path.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IntervalWorker failed: {ex}");
            }
        }

        /// <summary>
        /// Parses the current chat log and saves it. Called by both workers.
        /// </summary>
        private static void ParseThenSaveToFile(bool gameClosed = false)
        {
            try
            {
                AppController.InitializeServerIp();

                string parsed = AppController.ParseChatLog(directoryPath, Properties.Settings.Default.RemoveTimestampsFromBackup, gameClosed);
                if (string.IsNullOrWhiteSpace(parsed))
                    return;

                // First line of the chat log: [DATE: 14/NOV/2018 | TIME: 15:44:39]
                string fileName = parsed.Substring(0, parsed.IndexOf("\n", StringComparison.Ordinal));

                string fileNameDate = Regex.Match(fileName, @"\d{1,2}\/[A-Za-z]{3}\/\d{4}").ToString().Replace("/", ".");
                string year = Regex.Match(fileNameDate, @"\d{4}").ToString();
                string month = Regex.Match(fileNameDate, @"[A-Za-z]{3}").ToString();
                string fileNameTime = Regex.Match(fileName, @"\d{1,2}:\d{1,2}:\d{1,2}").ToString().Replace(":", ".");

                if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(fileNameDate) || string.IsNullOrWhiteSpace(fileNameTime) || string.IsNullOrWhiteSpace(year) || string.IsNullOrWhiteSpace(month))
                    throw new IOException("Chat log header did not match expected DATE/TIME format.");

                // Final name: 14.NOV.2018-15.44.39.txt, bucketed by year/month
                fileName = $"{fileNameDate}-{fileNameTime}.txt";
                string directory = Path.Combine(backupPath, year, month);
                string finalPath = Path.Combine(directory, fileName);
                string tempPath = Path.Combine(directory, ".temp");

                Directory.CreateDirectory(directory);

                if (!File.Exists(finalPath))
                {
                    File.WriteAllText(finalPath, parsed.Replace("\n", Environment.NewLine));
                }
                else
                {
                    // File from a prior interval write exists — keep whichever copy is larger
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);

                    File.WriteAllText(tempPath, parsed.Replace("\n", Environment.NewLine));

                    long oldLen = new FileInfo(finalPath).Length;
                    long newLen = new FileInfo(tempPath).Length;

                    if (oldLen < newLen)
                    {
                        File.Delete(finalPath);
                        File.Move(tempPath, finalPath);
                    }
                    else
                    {
                        File.Delete(tempPath);
                    }
                }

                if (!gameClosed) return;
                if (!Properties.Settings.Default.SuppressNotifications)
                    DisplayBackupResultMessage(string.Format(Strings.SuccessfulBackup, finalPath), Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);

                if (Properties.Settings.Default.WarnOnSameHash)
                    HashGenerator.SaveParsedHash(parsed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseThenSaveToFile failed: {ex}");
                if (gameClosed)
                    DisplayBackupResultMessage(Strings.BackupError, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
