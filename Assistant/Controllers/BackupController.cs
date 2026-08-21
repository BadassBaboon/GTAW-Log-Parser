using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Assistant.Localization;
using Assistant.Utilities;
using GTAWParser.Shared;
using Serilog;

namespace Assistant.Controllers
{
    public static class BackupController
    {
        private const int GameClosedCheckTime = 5;

        private static CancellationTokenSource? _cts;
        private static Task? _backupTask;
        private static Task? _intervalTask;

        private static string backupPath = string.Empty;
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
            backupPath = Properties.Settings.Default.BackupPath;

            bool enableAutomaticBackup = Properties.Settings.Default.BackupChatLogAutomatically;
            bool enableIntervalBackup = Properties.Settings.Default.EnableIntervalBackup;

            if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
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
            catch (Exception ex) { Log.Error(ex, "AbortAll failed"); }
        }

        private static async Task BackupWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    bool running = AppController.IsFiveMRunning();

                    if (!isGameRunning && running)
                        isGameRunning = true;
                    else if (isGameRunning && !running)
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
                Log.Error(ex, "BackupWorker failed");
            }
        }

        private static async Task IntervalWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int intervalMinutes = Math.Max(1, Properties.Settings.Default.IntervalTime);

                    if (isGameRunning && File.Exists(FiveMChatCaptureService.SessionFilePath))
                        ParseThenSaveToFile(false);

                    await Task.Delay(intervalMinutes * 60_000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation path.
            }
            catch (Exception ex)
            {
                Log.Error(ex, "IntervalWorker failed");
            }
        }

        /// <summary>
        /// Parses the current chat log and saves it. Called by both workers.
        /// </summary>
        private static void ParseThenSaveToFile(bool gameClosed = false)
        {
            try
            {
                string parsed = AppController.ParseChatLog(Properties.Settings.Default.RemoveTimestampsFromBackup, gameClosed);
                if (string.IsNullOrWhiteSpace(parsed))
                    return;

                DateTime sessionTime = FiveMChatCaptureService.SessionStartedAt;
                string datePart = sessionTime.ToString("dd.MMM.yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
                string timePart = sessionTime.ToString("HH.mm.ss", CultureInfo.InvariantCulture);
                string year = sessionTime.ToString("yyyy", CultureInfo.InvariantCulture);
                string month = sessionTime.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();

                string fileName = $"{datePart}-{timePart}.txt";
                string directory = Path.Combine(backupPath, year, month);
                string finalPath = Path.Combine(directory, fileName);
                string tempPath = Path.Combine(directory, ".temp");

                Directory.CreateDirectory(directory);

                if (!File.Exists(finalPath))
                {
                    File.WriteAllText(finalPath, parsed.Replace("\n", Environment.NewLine), Encoding.UTF8);
                }
                else
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);

                    File.WriteAllText(tempPath, parsed.Replace("\n", Environment.NewLine), Encoding.UTF8);

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
                Log.Error(ex, "ParseThenSaveToFile failed");
                if (gameClosed)
                    DisplayBackupResultMessage(Strings.BackupError, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
