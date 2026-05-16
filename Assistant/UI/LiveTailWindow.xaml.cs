using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Assistant.Controllers;
using GTAWParser.Shared;
using Serilog;

namespace Assistant.UI
{
    /// <summary>
    /// Watches the GTA World .storage file with FileSystemWatcher; on every
    /// change, re-parses and appends the diff (lines new since the last read)
    /// to a read-only text pane. Auto-scrolls to bottom.
    /// </summary>
    public partial class LiveTailWindow
    {
        private FileSystemWatcher? _watcher;
        private string _previousFullLog = string.Empty;
        private DateTime _lastChangeUtc = DateTime.MinValue;
        private static readonly TimeSpan ChangeDebounce = TimeSpan.FromMilliseconds(200);

        public LiveTailWindow()
        {
            InitializeComponent();
        }

        private void ToggleWatch_Click(object sender, RoutedEventArgs e)
        {
            if (_watcher == null)
                StartWatching();
            else
                StopWatching();
        }

        private void StartWatching()
        {
            string directoryPath = Properties.Settings.Default.DirectoryPath;
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                MessageBox.Show("Set the RAGEMP directory in the main window first.", "Live Tail",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppController.InitializeServerIp();
            string storagePath = Path.Combine(directoryPath, AppController.LogLocation);

            if (!File.Exists(storagePath))
            {
                MessageBox.Show($"No .storage file at\n{storagePath}\n\nLaunch the game and join GTA World first.",
                    "Live Tail", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Baseline: read the current full log so we only show NEW lines.
            _previousFullLog = ChatLogParser.Parse(directoryPath) ?? string.Empty;
            Tail.Text = string.Empty;

            string? watchDir = Path.GetDirectoryName(storagePath);
            string watchFile = Path.GetFileName(storagePath);
            if (watchDir == null || string.IsNullOrEmpty(watchFile))
            {
                MessageBox.Show("Could not resolve storage path.", "Live Tail",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _watcher = new FileSystemWatcher(watchDir, watchFile)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnStorageChanged;

            ToggleWatch.Content = "Stop";
            StatusLabel.Content = $"Watching {watchFile}";
            StatusLabel.Foreground = System.Windows.Media.Brushes.Green;

            Log.Information("Live tail started for {Path}", storagePath);
        }

        private void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnStorageChanged;
                _watcher.Dispose();
                _watcher = null;
            }

            ToggleWatch.Content = "Start";
            StatusLabel.Content = "Stopped";
            StatusLabel.Foreground = System.Windows.Media.Brushes.Gray;

            Log.Information("Live tail stopped");
        }

        private void OnStorageChanged(object sender, FileSystemEventArgs e)
        {
            // FileSystemWatcher fires multiple events for a single write on
            // many systems. Debounce to one per 200ms.
            DateTime now = DateTime.UtcNow;
            if (now - _lastChangeUtc < ChangeDebounce)
                return;
            _lastChangeUtc = now;

            Dispatcher.BeginInvoke(new Action(RefreshFromDisk));
        }

        private void RefreshFromDisk()
        {
            string directoryPath = Properties.Settings.Default.DirectoryPath;
            if (string.IsNullOrWhiteSpace(directoryPath)) return;

            // Small retry loop — the game may still be writing.
            string newLog = string.Empty;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    newLog = ChatLogParser.Parse(directoryPath) ?? string.Empty;
                    if (!string.IsNullOrEmpty(newLog))
                        break;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }

            if (string.IsNullOrEmpty(newLog)) return;

            string delta;
            if (newLog.Length >= _previousFullLog.Length && newLog.StartsWith(_previousFullLog, StringComparison.Ordinal))
            {
                delta = newLog.Substring(_previousFullLog.Length);
            }
            else
            {
                // The log was truncated or the player rejoined — show the whole thing.
                Tail.Text = string.Empty;
                delta = newLog;
            }

            _previousFullLog = newLog;

            if (string.IsNullOrEmpty(delta)) return;

            if (RemoveTimestamps.IsChecked == true)
                delta = ChatLogParser.StripTimestamps(delta);

            Tail.AppendText(delta.Replace("\n", Environment.NewLine));
            Tail.ScrollToEnd();
        }

        private void RemoveTimestamps_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // Re-render the whole baseline with/without timestamps so the
            // toggle takes effect immediately. Future deltas honour the
            // current state of the checkbox.
            if (string.IsNullOrEmpty(_previousFullLog)) return;

            string rendered = RemoveTimestamps.IsChecked == true
                ? ChatLogParser.StripTimestamps(_previousFullLog)
                : _previousFullLog;

            Tail.Text = rendered.Replace("\n", Environment.NewLine);
            Tail.ScrollToEnd();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Tail.Text = string.Empty;
        }

        private void Tail_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Counter == null) return;
            if (string.IsNullOrWhiteSpace(Tail.Text))
            {
                Counter.Text = "0 characters and 0 lines";
                return;
            }
            Counter.Text = $"{Tail.Text.Length} characters and {Tail.Text.Split('\n').Length} lines";
        }

        private void LiveTail_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopWatching();
        }
    }
}
