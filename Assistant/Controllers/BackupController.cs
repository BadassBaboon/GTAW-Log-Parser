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
        private static volatile bool isGameRunning;
        private static volatile bool _hasUnsavedChanges;
        private static string _lastSavedNormalizedContent = string.Empty;

        private static bool _quitting;
        public static bool Quitting
        {
            get => _quitting;
            set
            {
                _quitting = value;
                if (value)
                {
                    try
                    {
                        if ((Properties.Settings.Default.BackupChatLogAutomatically || Properties.Settings.Default.EnableIntervalBackup) && 
                            (isGameRunning || _hasUnsavedChanges) &&
                            File.Exists(FiveMChatCaptureService.SessionFilePath) && 
                            new FileInfo(FiveMChatCaptureService.SessionFilePath).Length > 0)
                        {
                            ParseThenSaveToFile(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to flush backup on tool exit");
                    }

                    AbortAll();
                }
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

            FiveMChatCaptureService.LineReceived -= OnLineReceived;
            FiveMChatCaptureService.LineReceived += OnLineReceived;

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

        private static void OnLineReceived(string line)
        {
            _hasUnsavedChanges = true;
        }

        /// <summary>
        /// Signals both workers to stop at their next await point.
        /// </summary>
        public static void AbortAll()
        {
            try 
            { 
                FiveMChatCaptureService.LineReceived -= OnLineReceived;
                _cts?.Cancel(); 
            }
            catch (Exception ex) { Log.Error(ex, "AbortAll failed"); }
        }

        private static readonly object _saveLock = new object();

        private static async Task BackupWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        bool running = AppController.IsFiveMRunning();

                        if (!isGameRunning && running)
                        {
                            isGameRunning = true;
                            _hasUnsavedChanges = true;
                        }
                        else if (isGameRunning && !running)
                        {
                            isGameRunning = false;
                            ParseThenSaveToFile(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "BackupWorker iteration failed, continuing");
                    }

                    await Task.Delay(GameClosedCheckTime * 1000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation path.
            }
        }

        private static async Task IntervalWorkerAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int intervalMinutes = Math.Max(1, Properties.Settings.Default.IntervalTime);

                    await Task.Delay(intervalMinutes * 60_000, ct).ConfigureAwait(false);

                    try
                    {
                        if (isGameRunning && File.Exists(FiveMChatCaptureService.SessionFilePath))
                            ParseThenSaveToFile(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "IntervalWorker iteration failed, continuing");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation path.
            }
        }

        /// <summary>
        /// Parses the current chat log and saves it. Called by both workers.
        /// Thread-safe: guarded by _saveLock to prevent concurrent file writes.
        /// </summary>
        private static void ParseThenSaveToFile(bool gameClosed = false)
        {
            lock (_saveLock)
            {
                try
                {
                    backupPath = Properties.Settings.Default.BackupPath;
                    if (string.IsNullOrWhiteSpace(backupPath) || !Directory.Exists(backupPath))
                        return;

                    string parsed = AppController.ParseChatLog(Properties.Settings.Default.RemoveTimestampsFromBackup, showError: false);
                    if (string.IsNullOrWhiteSpace(parsed))
                        return;

                    string normalized = ChatLogParser.NormalizeLineEndings(parsed);
                    if (!gameClosed && !_hasUnsavedChanges && !string.IsNullOrEmpty(_lastSavedNormalizedContent) && string.Equals(_lastSavedNormalizedContent, normalized, StringComparison.Ordinal))
                    {
                        // Content is already saved and unchanged
                        return;
                    }

                    DateTime sessionTime = FiveMChatCaptureService.SessionStartedAt;
                    string datePart = sessionTime.ToString("dd.MMM.yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
                    string timePart = sessionTime.ToString("HH.mm.ss", CultureInfo.InvariantCulture);
                    string year = sessionTime.ToString("yyyy", CultureInfo.InvariantCulture);
                    string month = sessionTime.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();

                    int backupFormat = Properties.Settings.Default.BackupFormat;
                    bool removeTimestamps = Properties.Settings.Default.RemoveTimestampsFromBackup;
                    string baseFileName = $"{datePart}-{timePart}";
                    string directory = Path.Combine(backupPath, year, month);
                    Directory.CreateDirectory(directory);

                    string primarySavedPath = string.Empty;

                    // 1. Plain Text Backup (.txt)
                    if (backupFormat == 0 || backupFormat == 2)
                    {
                        string txtPath = Path.Combine(directory, $"{baseFileName}.txt");
                        string txtTemp = Path.Combine(directory, $".temp_{baseFileName}.txt");

                        WriteBackupFileWithDeduplication(txtPath, txtTemp, normalized);
                        primarySavedPath = txtPath;
                    }

                    // 2. Rich HTML Backup (.html)
                    if (backupFormat == 1 || backupFormat == 2)
                    {
                        string htmlContent;
                        var richLines = FiveMChatCaptureService.SessionRichLines;
                        if (richLines != null && richLines.Count > 0)
                        {
                            htmlContent = ChatLogHtmlExporter.GenerateHtml(richLines, removeTimestamps, $"GTAW Chat Log - {datePart}");
                        }
                        else
                        {
                            htmlContent = ChatLogHtmlExporter.GenerateHtmlFromText(parsed, removeTimestamps, $"GTAW Chat Log - {datePart}");
                        }

                        string htmlPath = Path.Combine(directory, $"{baseFileName}.html");
                        string htmlTemp = Path.Combine(directory, $".temp_{baseFileName}.html");

                        WriteBackupFileWithDeduplication(htmlPath, htmlTemp, htmlContent);
                        if (string.IsNullOrEmpty(primarySavedPath))
                            primarySavedPath = htmlPath;
                    }

                    _lastSavedNormalizedContent = normalized;
                    _hasUnsavedChanges = false;

                    if (!gameClosed) return;
                    if (!Properties.Settings.Default.SuppressNotifications)
                        DisplayBackupResultMessage(string.Format(Strings.SuccessfulBackup, primarySavedPath), Strings.Information, MessageBoxButton.OK, MessageBoxImage.Information);

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

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == (char)10) count++;   // newline
            }
            return count;
        }

        private static void WriteBackupFileWithDeduplication(string finalPath, string tempPath, string content)
        {
            if (!File.Exists(finalPath))
            {
                File.WriteAllText(finalPath, content, Encoding.UTF8);
            }
            else
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                File.WriteAllText(tempPath, content, Encoding.UTF8);

                // Deciding by byte length alone is wrong in one important case: after the line
                // ending fix, a correctly normalised file is *shorter* than the same log written
                // with the old doubled CRLFs, so the good content would be discarded and the
                // corrupted file kept forever. Compare what the files actually contain instead.
                bool replace;
                try
                {
                    string existing = File.ReadAllText(finalPath, Encoding.UTF8);
                    string existingNormalized = ChatLogParser.NormalizeLineEndings(existing);

                    if (string.Equals(existingNormalized, content, StringComparison.Ordinal))
                    {
                        // Same log. Rewrite only if the file on disk still has the old spacing.
                        replace = !string.Equals(existing, content, StringComparison.Ordinal);
                    }
                    else
                    {
                        // Otherwise keep the guard that protects a full backup from being replaced
                        // by a truncated one, but count lines rather than bytes.
                        replace = CountLines(content) >= CountLines(existingNormalized);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not read existing backup {Path}; falling back to size comparison.", finalPath);
                    replace = new FileInfo(finalPath).Length < new FileInfo(tempPath).Length;
                }

                if (replace)
                {
                    File.Delete(finalPath);
                    File.Move(tempPath, finalPath);
                }
                else
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
